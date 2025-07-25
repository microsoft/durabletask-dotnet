// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Tests.Logging;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Microsoft.DurableTask.Grpc.Tests;

public class OrchestrationPatterns : IntegrationTestBase
{
    public OrchestrationPatterns(ITestOutputHelper output, GrpcSidecarFixture sidecarFixture)
        : base(output, sidecarFixture)
    { }

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

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
    }

    [Fact]
    public async Task ScheduleOrchesrationWithTags()
    {
        TaskName orchestratorName = nameof(EmptyOrchestration);
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc(orchestratorName, ctx => Task.FromResult<object?>(null)));
        });

        // Schedule a new orchestration instance with tags
        StartOrchestrationOptions options = new()
        {
            Tags = new Dictionary<string, string>
            {
                { "tag1", "value1" },
                { "tag2", "value2" }
            }
        };
        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, options);

        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.NotNull(metadata.Tags);
        Assert.Equal(2, metadata.Tags.Count);
        Assert.Equal("value1", metadata.Tags["tag1"]);
        Assert.Equal("value2", metadata.Tags["tag2"]);
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

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);

        // Verify that the delay actually happened with a 1 second variation
        Assert.True(metadata.CreatedAt.Add(delay) <= metadata.LastUpdatedAt.AddSeconds(1));
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

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);

        // Verify that the delay actually happened
        Assert.True(metadata.CreatedAt.Add(delay) <= metadata.LastUpdatedAt.AddSeconds(1));

        // Verify that the correct number of timers were created
        IReadOnlyCollection<LogEntry> logs = this.GetLogs();
        int timersCreated = logs.Count(log => log.Message.Contains("CreateTimer"));
        Assert.Equal(ExpectedTimers, timersCreated);
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
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.True(metadata.ReadOutputAs<bool>());
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
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("Hello, World!", metadata.ReadOutputAs<string>());

        IReadOnlyCollection<LogEntry> workerLogs = this.GetLogs("Microsoft.DurableTask.Worker");
        Assert.NotEmpty(workerLogs);

        // Validate logs.
        Assert.Single(workerLogs, log => MatchLog(
            log,
            logEventName: "OrchestrationStarted",
            exception: null,
            ("InstanceId", instanceId),
            ("Name", orchestratorName.Name)));

        Assert.Single(workerLogs, log => MatchLog(
            log,
            logEventName: "ActivityStarted",
            exception: null,
            ("InstanceId", instanceId),
            ("Name", sayHelloActivityName.Name)));

        Assert.Single(workerLogs, log => MatchLog(
            log,
            logEventName: "ActivityCompleted",
            exception: null,
            ("InstanceId", instanceId),
            ("Name", sayHelloActivityName.Name)));

        Assert.Single(workerLogs, log => MatchLog(
            log,
            logEventName: "OrchestrationCompleted",
            exception: null,
            ("InstanceId", instanceId),
            ("Name", orchestratorName.Name)));
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
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("Hello, World!", metadata.ReadOutputAs<string>());
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
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal(10, metadata.ReadOutputAs<int>());
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
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc(orchestratorName, async ctx =>
            {
                List<int> events = new();
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
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);

        int[] expected = Enumerable.Range(0, eventCount).ToArray();
        Assert.Equal<int>(expected, metadata.ReadOutputAs<int[]>());
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
                List<Task<int>> events = new();
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
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);

        int[] expected = Enumerable.Range(0, eventCount).ToArray();
        Assert.Equal<int>(expected, metadata.ReadOutputAs<int[]>());
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
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal(10, metadata.ReadOutputAs<int>());
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
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal(15, metadata.ReadOutputAs<int>());
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
        Assert.NotNull(metadata);
        Assert.Equal("Started!", metadata.ReadCustomStatusAs<string>());

        // Send a tuple payload, which will be used as the custom status
        (string, int) eventPayload = ("Hello", 42);
        await server.Client.RaiseEventAsync(
            metadata.InstanceId,
            eventName: "StatusEvent",
            eventPayload);

        // Once the orchestration receives all the events it is expecting, it should complete.
        metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal(eventPayload, metadata.ReadCustomStatusAs<(string, int)>());
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
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.True(metadata.ReadOutputAs<bool>());
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

        Assert.NotNull(output);
        Assert.Equal("original value", output?["originalProperty"]?.ToString());
        Assert.Equal("new value", output?["newProperty"]?.ToString());
    }

    // TODO: Additional versioning tests
    [Fact]
    public async Task OrchestrationVersionPassedThroughContext()
    {
        var version = "0.1";
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc<string, string>("Versioned_Orchestration", (ctx, input) =>
                {
                    return ctx.CallActivityAsync<string>("Versioned_Activity", ctx.Version);
                })
                .AddActivityFunc<string, string>("Versioned_Activity", (ctx, input) =>
                {
                    return $"Orchestration version: {input}";
                }));
        }, c =>
        {
            c.UseDefaultVersion(version);
        });

        var instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync("Versioned_Orchestration", input: string.Empty);
        var result = await server.Client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        var output = result.ReadOutputAs<string>();

        Assert.NotNull(output);
        Assert.Equal(output, $"Orchestration version: {version}");
    }

    [Fact]
    public async Task OrchestrationVersioning_MatchTypeNotSpecified_NoVersionFailure()
    {
        var workerVersion = "0.1";
        var clientVersion = "0.2";
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc<string, string>("Versioned_Orchestration", (ctx, input) =>
                {
                    return ctx.CallActivityAsync<string>("Versioned_Activity", ctx.Version);
                })
                .AddActivityFunc<string, string>("Versioned_Activity", (ctx, input) =>
                {
                    return $"Orchestration version: {input}";
                }));
            b.UseVersioning(new()
            {
                Version = workerVersion,
                FailureStrategy = DurableTaskWorkerOptions.VersionFailureStrategy.Fail
            });
        }, c =>
        {
            c.UseDefaultVersion(clientVersion);
        });

        var instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync("Versioned_Orchestration", input: string.Empty);
        var result = await server.Client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        var output = result.ReadOutputAs<string>();

        Assert.NotNull(output);
        // The worker doesn't pass it's version through the context, so we check the client version. The fact that it passed indicates versioning was ignored.
        Assert.Equal(output, $"Orchestration version: {clientVersion}");
    }

    [Fact]
    public async Task OrchestrationVersioning_MatchTypeNone_NoVersionFailure()
    {
        var workerVersion = "0.1";
        var clientVersion = "0.2";
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc<string, string>("Versioned_Orchestration", (ctx, input) =>
                {
                    return ctx.CallActivityAsync<string>("Versioned_Activity", ctx.Version);
                })
                .AddActivityFunc<string, string>("Versioned_Activity", (ctx, input) =>
                {
                    return $"Orchestration version: {input}";
                }));
            b.UseVersioning(new()
            {
                Version = workerVersion,
                MatchStrategy = DurableTaskWorkerOptions.VersionMatchStrategy.None,
                FailureStrategy = DurableTaskWorkerOptions.VersionFailureStrategy.Fail
            });
        }, c =>
        {
            c.UseDefaultVersion(clientVersion);
        });

        var instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync("Versioned_Orchestration", input: string.Empty);
        var result = await server.Client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        var output = result.ReadOutputAs<string>();

        Assert.NotNull(output);
        // The worker doesn't pass it's version through the context, so we check the client version. The fact that it passed indicates versioning was ignored.
        Assert.Equal(output, $"Orchestration version: {clientVersion}");
    }

    [Fact]
    public async Task OrchestrationVersioning_MatchTypeStrict_VersionFailure()
    {
        var workerVersion = "0.1";
        var clientVersion = "0.2";
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc<string, string>("Versioned_Orchestration", (ctx, input) =>
                {
                    return ctx.CallActivityAsync<string>("Versioned_Activity", ctx.Version);
                })
                .AddActivityFunc<string, string>("Versioned_Activity", (ctx, input) =>
                {
                    return $"Orchestration version: {input}";
                }));
            b.UseVersioning(new()
            {
                Version = workerVersion,
                MatchStrategy = DurableTaskWorkerOptions.VersionMatchStrategy.Strict,
                FailureStrategy = DurableTaskWorkerOptions.VersionFailureStrategy.Fail
            });
        }, c =>
        {
            c.UseDefaultVersion(clientVersion);
        });

        var instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync("Versioned_Orchestration", input: string.Empty);
        var result = await server.Client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(result);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, result.RuntimeStatus);
        Assert.NotNull(result.FailureDetails);
        Assert.Equal("VersionMismatch", result.FailureDetails.ErrorType);
    }

    [Fact]
    public async Task OrchestrationVersioning_MatchTypeCurrentOrOlder_VersionFailure()
    {
        var workerVersion = "0.1";
        var clientVersion = "0.2";
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc<string, string>("Versioned_Orchestration", (ctx, input) =>
                {
                    return ctx.CallActivityAsync<string>("Versioned_Activity", ctx.Version);
                })
                .AddActivityFunc<string, string>("Versioned_Activity", (ctx, input) =>
                {
                    return $"Orchestration version: {input}";
                }));
            b.UseVersioning(new()
            {
                Version = workerVersion,
                MatchStrategy = DurableTaskWorkerOptions.VersionMatchStrategy.CurrentOrOlder,
                FailureStrategy = DurableTaskWorkerOptions.VersionFailureStrategy.Fail
            });
        }, c =>
        {
            c.UseDefaultVersion(clientVersion);
        });

        var instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync("Versioned_Orchestration", input: string.Empty);
        var result = await server.Client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(result);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, result.RuntimeStatus);
        Assert.NotNull(result.FailureDetails);
        Assert.Equal("VersionMismatch", result.FailureDetails.ErrorType);
    }

    [Fact]
    public async Task OrchestrationVersioning_MatchTypeCurrentOrOlder_VersionSuccess()
    {
        var workerVersion = "0.3";
        var clientVersion = "0.2";
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc<string, string>("Versioned_Orchestration", (ctx, input) =>
                {
                    return ctx.CallActivityAsync<string>("Versioned_Activity", ctx.Version);
                })
                .AddActivityFunc<string, string>("Versioned_Activity", (ctx, input) =>
                {
                    return $"Orchestration version: {input}";
                }));
            b.UseVersioning(new()
            {
                Version = workerVersion,
                MatchStrategy = DurableTaskWorkerOptions.VersionMatchStrategy.CurrentOrOlder,
                FailureStrategy = DurableTaskWorkerOptions.VersionFailureStrategy.Fail
            });
        }, c =>
        {
            c.UseDefaultVersion(clientVersion);
        });

        var instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync("Versioned_Orchestration", input: string.Empty);
        var result = await server.Client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        var output = result.ReadOutputAs<string>();

        Assert.NotNull(output);
        // The worker doesn't pass it's version through the context, so we check the client version. The fact that it passed indicates versioning was ignored.
        Assert.Equal(output, $"Orchestration version: {clientVersion}");
    }

    [Fact]
    public async Task SubOrchestrationInheritsDefaultVersion()
    {
        var version = "0.1";
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc<string, string>("Versioned_Orchestration", (ctx, input) =>
                {
                    return ctx.CallSubOrchestratorAsync<string>("Versioned_Sub_Orchestration");
                })
                .AddOrchestratorFunc<string, string>("Versioned_Sub_Orchestration", (ctx, input) =>
                {
                    return ctx.CallActivityAsync<string>("Versioned_Activity", ctx.Version);
                })
                .AddActivityFunc<string, string>("Versioned_Activity", (ctx, input) =>
                {
                    return $"Sub Orchestration version: {input}";
                }));
            b.UseVersioning(new DurableTaskWorkerOptions.VersioningOptions
            {
                DefaultVersion = version
            });
        }, c =>
        {
            c.UseDefaultVersion(version);
        });

        var instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync("Versioned_Orchestration", input: string.Empty);
        var result = await server.Client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        var output = result.ReadOutputAs<string>();

        Assert.NotNull(output);
        Assert.Equal($"Sub Orchestration version: {version}", output);
    }

    [Theory]
    [InlineData("0.2")]
    [InlineData("")]
    public async Task OrchestrationTaskVersionOverridesDefaultVersion(string overrideVersion)
    {
        var version = "0.1";
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc<string, string>("Versioned_Orchestration", (ctx, input) =>
                {
                    return ctx.CallActivityAsync<string>("Versioned_Activity", ctx.Version);
                })
                .AddActivityFunc<string, string>("Versioned_Activity", (ctx, input) =>
                {
                    return $"Orchestration version: {input}";
                }));
        }, c =>
        {
            c.UseDefaultVersion(version);
        });

        var instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync("Versioned_Orchestration", string.Empty, new StartOrchestrationOptions
        {
            Version = overrideVersion
        });
        var result = await server.Client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        var output = result.ReadOutputAs<string>();

        Assert.NotNull(output);
        Assert.Equal($"Orchestration version: {overrideVersion}", output);
    }

    [Theory]
    [InlineData("0.2")]
    [InlineData("")]
    public async Task SubOrchestrationTaskVersionOverridesDefaultVersion(string overrideVersion)
    {
        var version = "0.1";
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc<string, string>("Versioned_Orchestration", (ctx, input) =>
                {
                    return ctx.CallSubOrchestratorAsync<string>("Versioned_Sub_Orchestration", new SubOrchestrationOptions
                    {
                        Version = overrideVersion
                    });
                })
                .AddOrchestratorFunc<string, string>("Versioned_Sub_Orchestration", (ctx, input) =>
                {
                    return ctx.CallActivityAsync<string>("Versioned_Activity", ctx.Version);
                })
                .AddActivityFunc<string, string>("Versioned_Activity", (ctx, input) =>
                {
                    return $"Sub Orchestration version: {input}";
                }));
            b.UseVersioning(new DurableTaskWorkerOptions.VersioningOptions
            {
                DefaultVersion = version,
            });
        }, c =>
        {
            c.UseDefaultVersion(version);
        });

        var instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync("Versioned_Orchestration", input: string.Empty);
        var result = await server.Client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        var output = result.ReadOutputAs<string>();

        Assert.NotNull(output);
        Assert.Equal($"Sub Orchestration version: {overrideVersion}", output);
    }

    [Fact]
    public async Task RunActivityWithTags()
    {
        TaskName orchestratorName = nameof(RunActivityWithTags);
        TaskName taggedActivityName = "TaggedActivity";

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc<string, string>(
                    orchestratorName, (ctx, input) => ctx.CallActivityAsync<string>(taggedActivityName, input))
                .AddActivityFunc<string, string>(taggedActivityName, (ctx, name) => $"Hello from tagged activity, {name}!"));
        });

        // Schedule orchestration with tags
        StartOrchestrationOptions options = new()
        {
            Tags = new Dictionary<string, string>
            {
                { "activityTag", "taggedExecution" },
                { "testType", "activityTagTest" }
            }
        };

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            orchestratorName, input: "World", options);

        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("Hello from tagged activity, World!", metadata.ReadOutputAs<string>());
    }

    [Obsolete("Experimental")]
    [Fact]
    public async Task FilterOrchestrationsByName()
    {
        // Setup a worker with an Orchestration Filter.
        TaskName orchestratorName = nameof(EmptyOrchestration);
        var orchestrationFilter = new OrchestrationFilter();
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc(orchestratorName, ctx => Task.FromResult<object?>(null)));
            b.UseOrchestrationFilter(orchestrationFilter);
        });

        // Nothing in the filter set, the orchestration should complete.
        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);

        // Update the filter and re-enqueue. We should see the orchestration denied.
        orchestrationFilter.NameDenySet.Add(orchestratorName);

        // This should throw as the work is denied.
        instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await server.Client.WaitForInstanceCompletionAsync(instanceId, cts.Token));
    }

    [Obsolete("Experimental")]
    [Fact]
    public async Task FilterOrchestrationsByNamePassesWhenNotMatching()
    {
        // Setup a worker with an Orchestration Filter.
        TaskName orchestratorName = nameof(EmptyOrchestration);
        var orchestrationFilter = new OrchestrationFilter();
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc(orchestratorName, ctx => Task.FromResult<object?>(null)));
            b.UseOrchestrationFilter(orchestrationFilter);
        });

        // Nothing in the filter set, the orchestration should complete.
        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);

        // Update the filter and re-enqueue. The name doesn't match so the filter should be OK.
        orchestrationFilter.NameDenySet.Add($"not-{orchestratorName}");

        // This should throw as the work is denied.
        instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
    }

    [Obsolete("Experimental")]
    [Fact]
    public async Task FilterOrchestrationsByTag()
    {
        // Setup a worker with an Orchestration Filter.
        TaskName orchestratorName = nameof(EmptyOrchestration);
        IReadOnlyDictionary<string, string> orchestratorTags = new Dictionary<string, string>
        {
            { "test", "true" }
        };
        var orchestrationFilter = new OrchestrationFilter();
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc(orchestratorName, ctx => Task.FromResult<object?>(null)));
            b.UseOrchestrationFilter(orchestrationFilter);
        });

        // Nothing in the filter set, the orchestration should complete.
        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, new StartOrchestrationOptions
        {
            Tags = orchestratorTags,
        });
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);

        // Update the filter and re-enqueue. We should see the orchestration denied.
        orchestrationFilter.TagDenyDict.Add("test", "true");

        // This should throw as the work is denied.
        instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, new StartOrchestrationOptions
        {
            Tags = orchestratorTags,
        });
        using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await server.Client.WaitForInstanceCompletionAsync(instanceId, cts.Token));
    }

    [Obsolete("Experimental")]
    [Fact]
    public async Task FilterOrchestrationsByTagPassesWithNoMatch()
    {
        // Setup a worker with an Orchestration Filter.
        TaskName orchestratorName = nameof(EmptyOrchestration);
        IReadOnlyDictionary<string, string> orchestratorTags = new Dictionary<string, string>
        {
            { "test", "true" }
        };
        var orchestrationFilter = new OrchestrationFilter();
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc(orchestratorName, ctx => Task.FromResult<object?>(null)));
            b.UseOrchestrationFilter(orchestrationFilter);
        });

        // Nothing in the filter set, the orchestration should complete.
        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, new StartOrchestrationOptions
        {
            Tags = orchestratorTags,
        });
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);

        // Update the filter and re-enqueue. The tags don't match so the orchestration should be OK.
        orchestrationFilter.TagDenyDict.Add("test", "false");

        // This should throw as the work is denied.
        instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, new StartOrchestrationOptions
        {
            Tags = orchestratorTags,
        });
        metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
    }

    [Obsolete("Experimental")]
    class OrchestrationFilter : IOrchestrationFilter
    {
        public ISet<string> NameDenySet { get; set; } = new HashSet<string>();
        public IDictionary<string, string> TagDenyDict = new Dictionary<string, string>();

        public ValueTask<bool> IsOrchestrationValidAsync(OrchestrationFilterParameters info, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(
                !this.NameDenySet.Contains(info.Name)
                && !this.TagDenyDict.Any(kvp => info.Tags != null && info.Tags.ContainsKey(kvp.Key) && info.Tags[kvp.Key] == kvp.Value));
        }
    }

    // TODO: Test for multiple external events with the same name
    // TODO: Test for ContinueAsNew with external events that carry over
    // TODO: Test for catching activity exceptions of specific types
}
