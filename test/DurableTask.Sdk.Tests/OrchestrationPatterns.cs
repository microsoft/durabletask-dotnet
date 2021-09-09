//  ----------------------------------------------------------------------------------
//  Copyright Microsoft Corporation
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//  http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//  ----------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DurableTask.Grpc;
using Xunit;

namespace DurableTask.Tests;

public class OrchestrationPatterns : IDisposable
{
    readonly CancellationTokenSource testTimeoutSource = new(Debugger.IsAttached ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(10));

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

    [Fact]
    public async Task EmptyOrchestration()
    {
        await using TaskHubGrpcServer server = TaskHubGrpcServer.CreateBuilder()
            .AddTaskOrchestrator("NoOp", ctx => Task.FromResult<object?>(null))
            .Build();
        await server.StartAsync();

        TaskHubGrpcClient client = TaskHubGrpcClient.Create();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync("NoOp");
        OrchestrationMetadata metadata = await client.WaitForInstanceCompletionAsync(instanceId, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
    }

    [Fact]
    public async Task SingleTimer()
    {
        TimeSpan delay = TimeSpan.FromSeconds(3);

        await using TaskHubGrpcServer server = TaskHubGrpcServer.CreateBuilder()
            .AddTaskOrchestrator("SingleTimer", ctx => ctx.CreateTimer(delay, CancellationToken.None))
            .Build();
        await server.StartAsync();

        TaskHubGrpcClient client = TaskHubGrpcClient.Create();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync("SingleTimer");
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
        await using TaskHubGrpcServer server = TaskHubGrpcServer.CreateBuilder()
            .AddTaskOrchestrator(nameof(IsReplaying), async ctx =>
            {
                var list = new List<bool>();
                list.Add(ctx.IsReplaying);
                await ctx.CreateTimer(TimeSpan.Zero, CancellationToken.None);
                list.Add(ctx.IsReplaying);
                await ctx.CreateTimer(TimeSpan.Zero, CancellationToken.None);
                list.Add(ctx.IsReplaying);
                return list;
            })
            .Build();
        await server.StartAsync();

        TaskHubGrpcClient client = TaskHubGrpcClient.Create();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(nameof(IsReplaying));
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
        await using TaskHubGrpcServer server = TaskHubGrpcServer.CreateBuilder()
            .AddTaskOrchestrator(nameof(CurrentDateTimeUtc), async ctx =>
            {
                DateTime currentDate1 = ctx.CurrentDateTimeUtc;
                DateTime originalDate1 = await ctx.CallActivityAsync<DateTime>("Echo", currentDate1);
                if (currentDate1 != originalDate1)
                {
                    return false;
                }

                DateTime currentDate2 = ctx.CurrentDateTimeUtc;
                DateTime originalDate2 = await ctx.CallActivityAsync<DateTime>("Echo", currentDate2);
                if (currentDate2 != originalDate2)
                {
                    return false;
                }

                return currentDate1 != currentDate2;
            })
            .AddTaskActivity("Echo", ctx => ctx.GetInput<object>())
            .Build();
        await server.StartAsync();

        TaskHubGrpcClient client = TaskHubGrpcClient.Create();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(nameof(CurrentDateTimeUtc));
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
        await using TaskHubGrpcServer server = TaskHubGrpcServer.CreateBuilder()
            .AddTaskOrchestrator(nameof(SingleActivity), ctx => ctx.CallActivityAsync<string>("SayHello", ctx.GetInput<string>()))
            .AddTaskActivity("SayHello", ctx => $"Hello, {ctx.GetInput<string>()}!")
            .Build();
        await server.StartAsync();

        TaskHubGrpcClient client = TaskHubGrpcClient.Create();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(nameof(SingleActivity), input: "World");
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
        await using TaskHubGrpcServer server = TaskHubGrpcServer.CreateBuilder()
            .AddTaskOrchestrator(nameof(ActivityChain), async ctx =>
            {
                int value = 0;
                for (int i = 0; i < 10; i++)
                {
                    value = await ctx.CallActivityAsync<int>("PlusOne", value);
                }

                return value;
            })
            .AddTaskActivity("PlusOne", ctx => ctx.GetInput<int>() + 1)
            .Build();
        await server.StartAsync();

        TaskHubGrpcClient client = TaskHubGrpcClient.Create();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(nameof(ActivityChain), input: "World");
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

        await using TaskHubGrpcServer server = TaskHubGrpcServer.CreateBuilder()
            .AddTaskOrchestrator(nameof(OrchestratorException), ctx => throw new Exception(errorMessage))
            .Build();
        await server.StartAsync();

        TaskHubGrpcClient client = TaskHubGrpcClient.Create();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorException));
        OrchestrationMetadata metadata = await client.WaitForInstanceCompletionAsync(
            instanceId,
            this.TimeoutToken,
            getInputsAndOutputs: true);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);

        OrchestrationFailureDetails? failureDetails = metadata.ReadOutputAs<OrchestrationFailureDetails>();
        Assert.NotNull(failureDetails);
        Assert.Contains(errorMessage, failureDetails!.FullText);
    }

    [Fact]
    public async Task ActivityFanOut()
    {
        await using TaskHubGrpcServer server = TaskHubGrpcServer.CreateBuilder()
            .AddTaskOrchestrator(nameof(ActivityFanOut), async ctx =>
            {
                var tasks = new List<Task<string>>();
                for (int i = 0; i < 10; i++)
                {
                    tasks.Add(ctx.CallActivityAsync<string>("ToString", i));
                }

                string[] results = await Task.WhenAll(tasks);
                Array.Sort(results);
                Array.Reverse(results);
                return results;
            })
            .AddTaskActivity("ToString", ctx => ctx.GetInput<object>().ToString())
            .Build();
        await server.StartAsync();

        TaskHubGrpcClient client = TaskHubGrpcClient.Create();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(nameof(ActivityFanOut));
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
        await using TaskHubGrpcServer server = TaskHubGrpcServer.CreateBuilder()
            .AddTaskOrchestrator(nameof(ExternalEvents), async ctx =>
            {
                List<int> events = new();
                for (int i = 0; i < eventCount; i++)
                {
                    events.Add(await ctx.WaitForExternalEvent<int>($"Event{i}"));
                }

                return events;
            })
            .Build();
        await server.StartAsync();

        TaskHubGrpcClient client = TaskHubGrpcClient.Create();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(nameof(ExternalEvents));

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
}
