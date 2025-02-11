// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Tests.Logging;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using Microsoft.DurableTask.Client;

namespace Microsoft.DurableTask.Grpc.Tests;

public class OrchestrationPatterns(ITestOutputHelper output, GrpcSidecarFixture sidecarFixture)
    : IntegrationTestBase(output, sidecarFixture)
{
    [Fact]
    public async Task EmptyOrchestration()
    {
        TaskName orchestratorName = nameof(EmptyOrchestration);
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc(orchestratorName, ctx => Task.FromResult<object?>(null)));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, this.TimeoutToken);

        metadata.Should().NotBeNull();
        metadata.InstanceId.Should().Be(instanceId);
        metadata.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
    }

    [Fact]
    public async Task SingleTimer()
    {
        TaskName orchestratorName = nameof(SingleTimer);
        TimeSpan delay = TimeSpan.FromSeconds(3);

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc(
                orchestratorName, ctx => ctx.CreateTimer(delay, CancellationToken.None)));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, this.TimeoutToken);

        metadata.Should().NotBeNull();
        metadata.InstanceId.Should().Be(instanceId);
        metadata.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);

        // Verify that the delay actually happened with a 1 second variation
        metadata.LastUpdatedAt.AddSeconds(1).Should().BeOnOrAfter(metadata.CreatedAt.Add(delay));
    }

    [Fact]
    public async Task LongTimer()
    {
        TaskName orchestratorName = nameof(SingleTimer);
        TimeSpan delay = TimeSpan.FromSeconds(7);
        TimeSpan timerInterval = TimeSpan.FromSeconds(3);
        const int ExpectedTimers = 3; // two for 3 seconds and one for 1 second

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.Configure(opt => opt.MaximumTimerInterval = timerInterval);
            b.AddTasks(tasks => tasks.AddOrchestratorFunc(
                orchestratorName, ctx => ctx.CreateTimer(delay, CancellationToken.None)));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(instanceId, this.TimeoutToken);

        metadata.Should().NotBeNull();
        metadata.InstanceId.Should().Be(instanceId);
        metadata.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);

        // Verify that the delay actually happened
        (metadata.CreatedAt.Add(delay) <= metadata.LastUpdatedAt.AddSeconds(1)).Should().BeTrue();

        // Verify that the correct number of timers were created
        IReadOnlyCollection<LogEntry> logs = this.GetLogs();
        logs.Where(log => log.Message.Contains("CreateTimer")).Should().HaveCount(ExpectedTimers);
    }

    [Fact]
    public async Task IsReplaying()
    {
        TaskName orchestratorName = nameof(IsReplaying);

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc(orchestratorName, async ctx =>
            {
                var list = new List<bool> { ctx.IsReplaying };
                await ctx.CreateTimer(TimeSpan.Zero, CancellationToken.None);
                list.Add(ctx.IsReplaying);
                await ctx.CreateTimer(TimeSpan.Zero, CancellationToken.None);
                list.Add(ctx.IsReplaying);
                return list;
            }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        metadata.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);

        List<bool> results = metadata.ReadOutputAs<List<bool>>()!;
        results.Should().NotBeNull();
        results.Should().HaveCount(3);
        results.Should().BeEquivalentTo([ true, true, false ]);
    }

    [Fact]
    public async Task CurrentDateTimeUtc()
    {
        TaskName orchestratorName = nameof(CurrentDateTimeUtc);
        TaskName echoActivityName = "Echo";

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    DateTime currentDate1 = ctx.CurrentUtcDateTime;
                    DateTime originalDate1 = await ctx.CallActivityAsync<DateTime>(echoActivityName, currentDate1);
                    if (currentDate1 != originalDate1)
                    {
                        return false;
                    }

                    DateTime currentDate2 = ctx.CurrentUtcDateTime;
                    DateTime originalDate2 = await ctx.CallActivityAsync<DateTime>(echoActivityName, currentDate2);
                    if (currentDate2 != originalDate2)
                    {
                        return false;
                    }

                    return currentDate1 != currentDate2;
                })
                .AddActivityFunc<object, object>(echoActivityName, (ctx, input) => input));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        metadata.Should().NotBeNull();
        metadata.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
        metadata.ReadOutputAs<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task SingleActivity()
    {
        TaskName orchestratorName = nameof(SingleActivity);
        TaskName sayHelloActivityName = "SayHello";

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc<string, string>(
                    orchestratorName, (ctx, input) => ctx.CallActivityAsync<string>(sayHelloActivityName, input))
                .AddActivityFunc<string, string>(sayHelloActivityName, (ctx, name) => $"Hello, {name}!"));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, input: "World");
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        metadata.Should().NotBeNull();
        metadata.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
        metadata.ReadOutputAs<string>().Should().Be("Hello, World!");
    }

    [Fact]
    public async Task SingleActivity_Async()
    {
        TaskName orchestratorName = nameof(SingleActivity);
        TaskName sayHelloActivityName = "SayHello";

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc<string, string>(
                    orchestratorName, (ctx, input) => ctx.CallActivityAsync<string>(sayHelloActivityName, input))
                .AddActivityFunc<string, string>(
                    sayHelloActivityName, async (ctx, name) => await Task.FromResult($"Hello, {name}!")));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, input: "World");
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        metadata.Should().NotBeNull();
        metadata.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
        metadata.ReadOutputAs<string>().Should().Be("Hello, World!");
    }

    [Fact]
    public async Task ActivityChain()
    {
        TaskName orchestratorName = nameof(ActivityChain);
        TaskName plusOneActivityName = "PlusOne";

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    int value = 0;
                    for (int i = 0; i < 10; i++)
                    {
                        value = await ctx.CallActivityAsync<int>(plusOneActivityName, value);
                    }

                    return value;
                })
                .AddActivityFunc<int, int>(plusOneActivityName, (ctx, input) => input + 1));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, input: "World");
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        metadata.Should().NotBeNull();
        metadata.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
        metadata.ReadOutputAs<int>().Should().Be(10);
    }

    [Fact]
    public async Task ActivityFanOut()
    {
        TaskName orchestratorName = nameof(ActivityFanOut);
        TaskName toStringActivity = "ToString";

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc(orchestratorName, async ctx =>
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
                .AddActivityFunc<object, string?>(toStringActivity, (ctx, input) => input.ToString()));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        metadata.Should().NotBeNull();
        metadata.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
        metadata.ReadOutputAs<string[]>().Should().Equal(["9", "8", "7", "6", "5", "4", "3", "2", "1", "0"]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task ExternalEvents(int eventCount)
    {
        TaskName orchestratorName = nameof(ExternalEvents);
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc(orchestratorName, async ctx =>
            {
                List<int> events = [];
                for (int i = 0; i < eventCount; i++)
                {
                    events.Add(await ctx.WaitForExternalEvent<int>($"Event{i}"));
                }

                return events;
            }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);

        // To ensure consistency, wait for the instance to start before sending the events
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceStartAsync(
            instanceId,
            this.TimeoutToken);

        // Send events one-at-a-time to that we can better ensure ordered processing.
        for (int i = 0; i < eventCount; i++)
        {
            await server.Client.RaiseEventAsync(metadata.InstanceId, $"Event{i}", eventPayload: i);
        }

        // Once the orchestration receives all the events it is expecting, it should complete.
        metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        metadata.Should().NotBeNull();
        metadata.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
        metadata.ReadOutputAs<int[]>().Should().Equal(Enumerable.Range(0, eventCount));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task ExternalEventsInParallel(int eventCount)
    {
        TaskName orchestratorName = nameof(ExternalEvents);
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc(orchestratorName, async ctx =>
            {
                List<Task<int>> events = [];
                for (int i = 0; i < eventCount; i++)
                {
                    events.Add(ctx.WaitForExternalEvent<int>("Event"));
                }

                return await Task.WhenAll(events);
            }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);

        // To ensure consistency, wait for the instance to start before sending the events
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceStartAsync(
            instanceId,
            this.TimeoutToken);

        // Send events one-at-a-time to that we can better ensure ordered processing.
        for (int i = 0; i < eventCount; i++)
        {
            await server.Client.RaiseEventAsync(metadata.InstanceId, "Event", eventPayload: i);
        }

        // Once the orchestration receives all the events it is expecting, it should complete.
        metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true);
        metadata.Should().NotBeNull();
        metadata.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
        metadata.ReadOutputAs<int[]>().Should().Equal(Enumerable.Range(0, eventCount));
    }

    [Fact]
    public async Task Termination()
    {
        TaskName orchestrationName = nameof(Termination);
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc(
                orchestrationName, ctx => ctx.CreateTimer(TimeSpan.FromSeconds(3), CancellationToken.None)));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestrationName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceStartAsync(instanceId, this.TimeoutToken);

        var expectedOutput = new { quote = "I'll be back." };
        await server.Client.TerminateInstanceAsync(instanceId, expectedOutput);

        metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        metadata.Should().NotBeNull();
        metadata.InstanceId.Should().Be(instanceId);
        metadata.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Terminated);

        JsonElement actualOutput = metadata.ReadOutputAs<JsonElement>();
        string? actualQuote = actualOutput.GetProperty("quote").GetString();
        actualQuote.Should().NotBeNull();
        actualQuote.Should().Be(expectedOutput.quote);
    }

    [Fact]
    public async Task ContinueAsNew()
    {
        TaskName orchestratorName = nameof(ContinueAsNew);

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc<int, int>(orchestratorName, async (ctx, input) =>
            {
                if (input < 10)
                {
                    await ctx.CreateTimer(TimeSpan.Zero, CancellationToken.None);
                    ctx.ContinueAsNew(input + 1);
                }

                return input;
            }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        metadata.Should().NotBeNull();
        metadata.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
        metadata.ReadOutputAs<int>().Should().Be(10);
    }

    [Fact]
    public async Task SubOrchestration()
    {
        TaskName orchestratorName = nameof(SubOrchestration);

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc<int, int>(orchestratorName, async (ctx, input) =>
            {
                int result = 5;
                if (input < 3)
                {
                    // recursively call this same orchestrator
                    result += await ctx.CallSubOrchestratorAsync<int>(orchestratorName, input: input + 1);
                }

                return result;
            }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, input: 1);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        metadata.Should().NotBeNull();
        metadata.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
        metadata.ReadOutputAs<int>().Should().Be(15);
    }

    [Fact]
    public async Task SetCustomStatus()
    {
        TaskName orchestratorName = nameof(SetCustomStatus);
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc(orchestratorName, async ctx =>
            {
                ctx.SetCustomStatus("Started!");

                object customStatus = await ctx.WaitForExternalEvent<object>("StatusEvent");
                ctx.SetCustomStatus(customStatus);
            }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);

        // To ensure consistency, wait for the instance to start before sending the events
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceStartAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        metadata.Should().NotBeNull();
        metadata.ReadCustomStatusAs<string>().Should().Be("Started!");

        // Send a tuple payload, which will be used as the custom status
        (string, int) eventPayload = ("Hello", 42);
        await server.Client.RaiseEventAsync(
            metadata.InstanceId,
            eventName: "StatusEvent",
            eventPayload);

        // Once the orchestration receives all the events it is expecting, it should complete.
        metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        metadata.Should().NotBeNull();
        metadata.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
        metadata.ReadCustomStatusAs<(string, int)>().Should().Be(eventPayload);
    }

    [Fact]
    public async Task NewGuidTest()
    {
        TaskName orchestratorName = nameof(ContinueAsNew);
        TaskName echoActivityName = "Echo";

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc<int, bool>(orchestratorName, async (ctx, input) =>
                {
                    // Test 1: Ensure two consecutively created GUIDs are unique
                    Guid currentGuid0 = ctx.NewGuid();
                    Guid currentGuid1 = ctx.NewGuid();
                    if (currentGuid0 == currentGuid1)
                    {
                        return false;
                    }

                    // Test 2: Ensure that the same GUID values are created on each replay
                    Guid originalGuid1 = await ctx.CallActivityAsync<Guid>(echoActivityName, currentGuid1);
                    if (currentGuid1 != originalGuid1)
                    {
                        return false;
                    }

                    // Test 3: Ensure that the same GUID values are created on each replay even after an await
                    Guid currentGuid2 = ctx.NewGuid();
                    Guid originalGuid2 = await ctx.CallActivityAsync<Guid>(echoActivityName, currentGuid2);
                    if (currentGuid2 != originalGuid2)
                    {
                        return false;
                    }

                    // Test 4: Finish confirming that every generated GUID is unique
                    return currentGuid1 != currentGuid2;
                })
                .AddActivityFunc<Guid, Guid>(echoActivityName, (ctx, input) => input));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        metadata.Should().NotBeNull();
        metadata.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
        metadata.ReadOutputAs<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task SpecialSerialization()
    {
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc<JsonNode, JsonNode>("SpecialSerialization_Orchestration", (ctx, input) =>
                {
                    if (input is null)
                    {
                        throw new ArgumentNullException(nameof(input));
                    }

                    return ctx.CallActivityAsync<JsonNode>("SpecialSerialization_Activity", input);
                })
                .AddActivityFunc<JsonNode, JsonNode?>("SpecialSerialization_Activity", (ctx, input) =>
                {
                    if (input is not null)
                    {
                        input["newProperty"] = "new value";
                    }

                    return Task.FromResult(input);
                }));
        });

        JsonNode input = new JsonObject() { ["originalProperty"] = "original value" };
        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            "SpecialSerialization_Orchestration", input: input);
        OrchestrationMetadata result = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        JsonNode? output = result.ReadOutputAs<JsonNode>();

        output.Should().NotBeNull();
        (output?["originalProperty"]?.ToString()).Should().Be("original value");
        (output?["newProperty"]?.ToString()).Should().Be("new value");
    }

    // TODO: Test for multiple external events with the same name
    // TODO: Test for ContinueAsNew with external events that carry over
    // TODO: Test for catching activity exceptions of specific types
    // TODO: Versioning tests
}
