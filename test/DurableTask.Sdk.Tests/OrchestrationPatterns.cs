// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DurableTask.Grpc;
using DurableTask.Sdk.Tests.Logging;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace DurableTask.Tests;

public class OrchestrationPatterns : IDisposable
{
    readonly CancellationTokenSource testTimeoutSource = new(Debugger.IsAttached ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(10));
    readonly ILoggerFactory loggerFactory;

    public OrchestrationPatterns(ITestOutputHelper output)
    {
        TestLogProvider logProvider = new(output);
        this.loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(logProvider);
            builder.SetMinimumLevel(LogLevel.Debug);
        });
    }

    /// <summary>
    /// Gets a <see cref="CancellationToken"/> that triggers after a default test timeout period.
    /// The actual timeout value is increased if a debugger is attached to the test process.
    /// </summary>
    public CancellationToken TimeoutToken => this.testTimeoutSource.Token;

    void IDisposable.Dispose()
    {
        this.testTimeoutSource.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Creates a <see cref="DurableTaskGrpcWorker"/> configured to output logs to xunit logging infrastructure.
    /// </summary>
    DurableTaskGrpcWorker.Builder CreateWorkerBuilder()
    {
        return DurableTaskGrpcWorker.CreateBuilder().UseLoggerFactory(this.loggerFactory);
    }

    /// <summary>
    /// Creates a <see cref="DurableTaskGrpcClient"/> configured to output logs to xunit logging infrastructure.
    /// </summary>
    DurableTaskClient CreateDurableTaskClient()
    {
        return DurableTaskGrpcClient.CreateBuilder().UseLoggerFactory(this.loggerFactory).Build();
    }

    [Fact]
    public async Task EmptyOrchestration()
    {
        TaskName orchestratorName = nameof(EmptyOrchestration);
        await using DurableTaskGrpcWorker server = this.CreateWorkerBuilder()
            .AddTasks(tasks => tasks.AddOrchestrator(orchestratorName, ctx => Task.FromResult<object?>(null)))
            .Build();
        await server.StartAsync(this.TimeoutToken);

        DurableTaskClient client = this.CreateDurableTaskClient();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await client.WaitForInstanceCompletionAsync(instanceId, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
    }

    [Fact]
    public async Task SingleTimer()
    {
        TaskName orchestratorName = nameof(SingleTimer);
        TimeSpan delay = TimeSpan.FromSeconds(3);

        await using DurableTaskGrpcWorker server = this.CreateWorkerBuilder()
            .AddTasks(tasks => tasks.AddOrchestrator(orchestratorName, ctx => ctx.CreateTimer(delay, CancellationToken.None)))
            .Build();
        await server.StartAsync(this.TimeoutToken);

        DurableTaskClient client = this.CreateDurableTaskClient();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await client.WaitForInstanceCompletionAsync(instanceId, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);

        // Verify that the delay actually happened
        Assert.True(metadata.CreatedAt.Add(delay) <= metadata.LastUpdatedAt);
    }

    [Fact]
    public async Task IsReplaying()
    {
        TaskName orchestratorName = nameof(IsReplaying);
        await using DurableTaskGrpcWorker server = this.CreateWorkerBuilder()
            .AddTasks(tasks => tasks.AddOrchestrator(orchestratorName, async ctx =>
            {
                var list = new List<bool>();
                list.Add(ctx.IsReplaying);
                await ctx.CreateTimer(TimeSpan.Zero, CancellationToken.None);
                list.Add(ctx.IsReplaying);
                await ctx.CreateTimer(TimeSpan.Zero, CancellationToken.None);
                list.Add(ctx.IsReplaying);
                return list;
            }))
            .Build();
        await server.StartAsync(this.TimeoutToken);

        DurableTaskClient client = this.CreateDurableTaskClient();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await client.WaitForInstanceCompletionAsync(
            instanceId,
            this.TimeoutToken,
            getInputsAndOutputs: true);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        List<bool>? results = metadata.ReadOutputAs<List<bool>>();
        Assert.NotNull(results);
        Assert.Equal(3, results!.Count);
        Assert.True(results[0]);
        Assert.True(results[1]);
        Assert.False(results[2]);
    }

    [Fact]
    public async Task CurrentDateTimeUtc()
    {
        TaskName orchestratorName = nameof(CurrentDateTimeUtc);
        TaskName echoActivityName = "Echo";

        await using DurableTaskGrpcWorker server = this.CreateWorkerBuilder()
            .AddTasks(tasks => tasks
                .AddOrchestrator(orchestratorName, async ctx =>
                {
                    DateTime currentDate1 = ctx.CurrentDateTimeUtc;
                    DateTime originalDate1 = await ctx.CallActivityAsync<DateTime>(echoActivityName, currentDate1);
                    if (currentDate1 != originalDate1)
                    {
                        return false;
                    }

                    DateTime currentDate2 = ctx.CurrentDateTimeUtc;
                    DateTime originalDate2 = await ctx.CallActivityAsync<DateTime>(echoActivityName, currentDate2);
                    if (currentDate2 != originalDate2)
                    {
                        return false;
                    }

                    return currentDate1 != currentDate2;
                })
                .AddActivity<object, object>(echoActivityName, (ctx, input) => input))
            .Build();
        await server.StartAsync(this.TimeoutToken);

        DurableTaskClient client = this.CreateDurableTaskClient();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await client.WaitForInstanceCompletionAsync(
            instanceId,
            this.TimeoutToken,
            getInputsAndOutputs: true);
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.True(metadata.ReadOutputAs<bool>());
    }

    [Fact]
    public async Task SingleActivity()
    {
        TaskName orchestratorName = nameof(SingleActivity);
        TaskName sayHelloActivityName = "SayHello";

        await using DurableTaskGrpcWorker server = this.CreateWorkerBuilder()
            .AddTasks(tasks => tasks
                .AddOrchestrator<string, string>(orchestratorName, (ctx, input) => ctx.CallActivityAsync<string?>(sayHelloActivityName, input))
                .AddActivity<string, string>(sayHelloActivityName, (ctx, name) => $"Hello, {name}!"))
            .Build();
        await server.StartAsync(this.TimeoutToken);

        DurableTaskClient client = this.CreateDurableTaskClient();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, input: "World");
        OrchestrationMetadata metadata = await client.WaitForInstanceCompletionAsync(
            instanceId,
            this.TimeoutToken,
            getInputsAndOutputs: true);
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("Hello, World!", metadata.ReadOutputAs<string>());
    }

    [Fact]
    public async Task SingleActivity_Async()
    {
        TaskName orchestratorName = nameof(SingleActivity);
        TaskName sayHelloActivityName = "SayHello";

        await using DurableTaskGrpcWorker server = this.CreateWorkerBuilder()
            .AddTasks(tasks => tasks
                .AddOrchestrator<string, string>(orchestratorName, (ctx, input) => ctx.CallActivityAsync<string?>(sayHelloActivityName, input))
                .AddActivity<string, string>(sayHelloActivityName, async (ctx, name) => await Task.FromResult($"Hello, {name}!")))
            .Build();
        await server.StartAsync(this.TimeoutToken);

        DurableTaskClient client = this.CreateDurableTaskClient();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, input: "World");
        OrchestrationMetadata metadata = await client.WaitForInstanceCompletionAsync(
            instanceId,
            this.TimeoutToken,
            getInputsAndOutputs: true);
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("Hello, World!", metadata.ReadOutputAs<string>());
    }

    [Fact]
    public async Task ActivityChain()
    {
        TaskName orchestratorName = nameof(ActivityChain);
        TaskName plusOneActivityName = "PlusOne";

        await using DurableTaskGrpcWorker server = this.CreateWorkerBuilder()
            .AddTasks(tasks => tasks
                .AddOrchestrator(orchestratorName, async ctx =>
                {
                    int value = 0;
                    for (int i = 0; i < 10; i++)
                    {
                        value = await ctx.CallActivityAsync<int>(plusOneActivityName, value);
                    }

                    return value;
                })
                .AddActivity<int, int>(plusOneActivityName, (ctx, input) => input + 1))
            .Build();
        await server.StartAsync(this.TimeoutToken);

        DurableTaskClient client = this.CreateDurableTaskClient();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, input: "World");
        OrchestrationMetadata metadata = await client.WaitForInstanceCompletionAsync(
            instanceId,
            this.TimeoutToken,
            getInputsAndOutputs: true);
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal(10, metadata.ReadOutputAs<int>());
    }

    [Fact]
    public async Task OrchestratorException()
    {
        string errorMessage = "Kah-BOOOOOM!!!";

        TaskName orchestratorName = nameof(OrchestratorException);
        await using DurableTaskGrpcWorker server = this.CreateWorkerBuilder()
            .AddTasks(tasks => tasks.AddOrchestrator(orchestratorName, ctx => throw new Exception(errorMessage)))
            .Build();
        await server.StartAsync(this.TimeoutToken);

        DurableTaskClient client = this.CreateDurableTaskClient();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await client.WaitForInstanceCompletionAsync(
            instanceId,
            this.TimeoutToken,
            getInputsAndOutputs: true);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);

        OrchestrationFailureDetails? failureDetails = metadata.ReadOutputAs<OrchestrationFailureDetails>();
        Assert.NotNull(failureDetails);
        Assert.Contains(errorMessage, failureDetails!.Details);
    }

    [Fact]
    public async Task ActivityFanOut()
    {
        TaskName orchestratorName = nameof(ActivityFanOut);
        TaskName toStringActivity = "ToString";

        await using DurableTaskGrpcWorker server = this.CreateWorkerBuilder()
            .AddTasks(tasks => tasks
                .AddOrchestrator(orchestratorName, async ctx =>
                {
                    var tasks = new List<Task<string>>();
                    for (int i = 0; i < 10; i++)
                    {
                        tasks.Add(ctx.CallActivityAsync<string>(toStringActivity, i));
                    }

                    string[] results = await Task.WhenAll(tasks);
                    Array.Sort(results);
                    Array.Reverse(results);
                    return results;
                })
                .AddActivity<object, string>(toStringActivity, (ctx, input) => input!.ToString()))
            .Build();
        await server.StartAsync(this.TimeoutToken);

        DurableTaskClient client = this.CreateDurableTaskClient();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await client.WaitForInstanceCompletionAsync(
            instanceId,
            this.TimeoutToken,
            getInputsAndOutputs: true);
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);

        string[] expected = new[] { "9", "8", "7", "6", "5", "4", "3", "2", "1", "0" };
        Assert.Equal<string>(expected, metadata.ReadOutputAs<string[]>());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task ExternalEvents(int eventCount)
    {
        TaskName orchestratorName = nameof(ExternalEvents);
        await using DurableTaskGrpcWorker server = this.CreateWorkerBuilder()
            .AddTasks(tasks => tasks.AddOrchestrator(orchestratorName, async ctx =>
            {
                List<int> events = new();
                for (int i = 0; i < eventCount; i++)
                {
                    events.Add(await ctx.WaitForExternalEvent<int>($"Event{i}"));
                }

                return events;
            }))
            .Build();
        await server.StartAsync(this.TimeoutToken);

        DurableTaskClient client = this.CreateDurableTaskClient();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);

        // To ensure consistency, wait for the instance to start before sending the events
        OrchestrationMetadata metadata = await client.WaitForInstanceStartAsync(
            instanceId,
            this.TimeoutToken);

        // Send events one-at-a-time to that we can better ensure ordered processing.
        for (int i = 0; i < eventCount; i++)
        {
            await client.RaiseEventAsync(metadata.InstanceId, $"Event{i}", eventPayload: i);
        }

        // Once the orchestration receives all the events it is expecting, it should complete.
        metadata = await client.WaitForInstanceCompletionAsync(
            instanceId,
            this.TimeoutToken,
            getInputsAndOutputs: true);
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);

        int[] expected = Enumerable.Range(0, eventCount).ToArray();
        Assert.Equal<int>(expected, metadata.ReadOutputAs<int[]>());
    }

    [Fact]
    public async Task Termination()
    {
        TaskName orchestrationName = nameof(Termination);
        await using DurableTaskGrpcWorker server = this.CreateWorkerBuilder()
            .AddTasks(tasks => tasks.AddOrchestrator(orchestrationName, ctx => ctx.CreateTimer(TimeSpan.FromSeconds(3), CancellationToken.None)))
            .Build();
        await server.StartAsync(this.TimeoutToken);

        DurableTaskClient client = this.CreateDurableTaskClient();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(orchestrationName);
        OrchestrationMetadata metadata = await client.WaitForInstanceStartAsync(instanceId, this.TimeoutToken);

        var expectedOutput = new { quote = "I'll be back." };
        await client.TerminateAsync(instanceId, expectedOutput);

        metadata = await client.WaitForInstanceCompletionAsync(
            instanceId,
            this.TimeoutToken,
            getInputsAndOutputs: true);
        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Terminated, metadata.RuntimeStatus);

        JsonElement actualOutput = metadata.ReadOutputAs<JsonElement>();
        string? actualQuote = actualOutput.GetProperty("quote").GetString();
        Assert.NotNull(actualQuote);
        Assert.Equal(expectedOutput.quote, actualQuote);
    }

    [Fact]
    public async Task ContinueAsNew()
    {
        TaskName orchestratorName = nameof(ContinueAsNew);

        await using DurableTaskGrpcWorker server = this.CreateWorkerBuilder()
            .AddTasks(tasks => tasks.AddOrchestrator<int, int>(orchestratorName, async (ctx, input) =>
            {
                if (input < 10)
                {
                    await ctx.CreateTimer(TimeSpan.Zero, CancellationToken.None);
                    ctx.ContinueAsNew(input + 1);
                }

                return input;
            }))
            .Build();
        await server.StartAsync(this.TimeoutToken);

        DurableTaskClient client = this.CreateDurableTaskClient();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await client.WaitForInstanceCompletionAsync(
            instanceId,
            this.TimeoutToken,
            getInputsAndOutputs: true);
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal(10, metadata.ReadOutputAs<int>());
    }

    [Fact]
    public async Task SubOrchestration()
    {
        TaskName orchestratorName = nameof(SubOrchestration);

        await using DurableTaskGrpcWorker server = this.CreateWorkerBuilder()
            .AddTasks(tasks => tasks.AddOrchestrator<int, int>(orchestratorName, async (ctx, input) =>
            {
                int result = 5;
                if (input < 3)
                {
                    // recursively call this same orchestrator
                    result += await ctx.CallSubOrchestratorAsync<int>(orchestratorName, input: input + 1);
                }

                return result;
            }))
            .Build();
        await server.StartAsync(this.TimeoutToken);

        DurableTaskClient client = this.CreateDurableTaskClient();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, input: 1);
        OrchestrationMetadata metadata = await client.WaitForInstanceCompletionAsync(
            instanceId,
            this.TimeoutToken,
            getInputsAndOutputs: true);
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal(15, metadata.ReadOutputAs<int>());
    }

    [Fact]
    public async Task SetCustomStatus()
    {
        TaskName orchestratorName = nameof(SetCustomStatus);
        await using DurableTaskGrpcWorker server = this.CreateWorkerBuilder()
            .AddTasks(tasks => tasks.AddOrchestrator(orchestratorName, async ctx =>
            {
                ctx.SetCustomStatus("Started!");

                object customStatus = await ctx.WaitForExternalEvent<object>("StatusEvent");
                ctx.SetCustomStatus(customStatus);
            }))
            .Build();
        await server.StartAsync(this.TimeoutToken);

        DurableTaskClient client = this.CreateDurableTaskClient();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);

        // To ensure consistency, wait for the instance to start before sending the events
        OrchestrationMetadata metadata = await client.WaitForInstanceStartAsync(
            instanceId,
            this.TimeoutToken,
            getInputsAndOutputs: true);
        Assert.NotNull(metadata);
        Assert.Equal("Started!", metadata.ReadCustomStatusAs<string>());

        // Send a tuple payload, which will be used as the custom status
        (string, int) eventPayload = ("Hello", 42);
        await client.RaiseEventAsync(
            metadata.InstanceId,
            eventName: "StatusEvent",
            eventPayload);

        // Once the orchestration receives all the events it is expecting, it should complete.
        metadata = await client.WaitForInstanceCompletionAsync(
            instanceId,
            this.TimeoutToken,
            getInputsAndOutputs: true);
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal(eventPayload, metadata.ReadCustomStatusAs<(string, int)>());
    }

    // TODO: Test for multiple external events with the same name
    // TODO: Test for ContinueAsNew with external events that carry over
    // TODO: Test for catching activity exceptions of specific types
    // TODO: Versioning tests
}
