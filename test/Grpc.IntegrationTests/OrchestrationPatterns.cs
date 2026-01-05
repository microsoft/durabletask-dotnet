// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
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
    public async Task ScheduleOrchestrationWithTags()
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
    public async Task ScheduleSubOrchestrationWithTags()
    {
        TaskName orchestratorName = nameof(ScheduleSubOrchestrationWithTags);

        // Schedule a new orchestration instance with tags
        SubOrchestrationOptions subOrchestrationOptions = new()
        {
            InstanceId = "instance_id",
            Tags = new Dictionary<string, string>
            {
                { "tag1", "value1" },
                { "tag2", "value2" }
            }
        };

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc<int, int>(orchestratorName, async (ctx, input) =>
            {
                int result = 1;
                if (input < 2)
                {
                    // recursively call this same orchestrator
                    result += await ctx.CallSubOrchestratorAsync<int>(orchestratorName, input: input + 1, subOrchestrationOptions);
                }

                return result;
            }));
        });


        await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, input: 1);

        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            subOrchestrationOptions.InstanceId, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.NotNull(metadata.Tags);
        Assert.Equal(2, metadata.Tags.Count);
        Assert.Equal("value1", metadata.Tags["tag1"]);
        Assert.Equal("value2", metadata.Tags["tag2"]);
    }

    [Fact]
    public async Task ScheduleSubOrchestrationWithTagsAndRetryPolicy()
    {
        TaskName orchestratorName = nameof(ScheduleSubOrchestrationWithTagsAndRetryPolicy);

        // Schedule a new orchestration instance with tags and a retry policy
        SubOrchestrationOptions subOrchestrationOptions = new()
        {
            InstanceId = "instance_id",
            Tags = new Dictionary<string, string>
            {
                { "tag1", "value1" },
                { "tag2", "value2" }
            },
            Retry = new RetryPolicy(maxNumberOfAttempts: 2, firstRetryInterval: TimeSpan.FromSeconds(15))
        };

        int failCounter = 0;
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc<int, int>(orchestratorName, async (ctx, input) =>
            {
                if (failCounter < 1 && input == 2)
                {
                    failCounter++;
                    throw new Exception("Simulated failure");
                }

                int result = 1;
                if (input < 2)
                {
                    // recursively call this same orchestrator
                    result += await ctx.CallSubOrchestratorAsync<int>(orchestratorName, input: input + 1, subOrchestrationOptions);
                }

                return result;
            }));
        });
        using CancellationTokenSource timeoutTokenSource = new(TimeSpan.FromMinutes(1));

        // Confirm the first attempt failed
        await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, input: 1);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            subOrchestrationOptions.InstanceId, timeoutTokenSource.Token);
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);

        // Wait for the retry to happen
        while (metadata.RuntimeStatus != OrchestrationRuntimeStatus.Completed && !timeoutTokenSource.Token.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), timeoutTokenSource.Token);
            metadata = await server.Client.WaitForInstanceCompletionAsync(
                subOrchestrationOptions.InstanceId, timeoutTokenSource.Token);
        }

        // Confirm the second attempt succeeded
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
        Assert.Equal((IEnumerable<string>)expected, metadata.ReadOutputAs<string[]>());
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

        // With Stack (LIFO) behavior, the most recent waiter receives events first.
        // So if we create waiters in order [0, 1, 2, 3, 4] and send events [0, 1, 2, 3, 4],
        // the waiters will receive them in reverse order: [4, 3, 2, 1, 0]
        int[] expected = Enumerable.Range(0, eventCount).Reverse().ToArray();
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
            bool nameAllowed = info.Name is string name && !this.NameDenySet.Contains(name);
            bool tagsAllowed = info.Tags == null
                || !this.TagDenyDict.Any(kvp => info.Tags.TryGetValue(kvp.Key, out string? value) && value == kvp.Value);

            return ValueTask.FromResult(nameAllowed && tagsAllowed);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ContinueAsNewEventsArePreserved(bool injectTimers)
    {
        const int EventCount = 10;
        async Task<int> OrchestratorFunc(TaskOrchestrationContext ctx, int counter)
        {
            await ctx.WaitForExternalEvent<string>("Event");
            counter++;

            if (injectTimers)
            {
                await ctx.CreateTimer(TimeSpan.FromMilliseconds(1), CancellationToken.None);
            }

            if (counter < EventCount)
            {
                ctx.ContinueAsNew(counter, preserveUnprocessedEvents: true);
            }

            return counter;
        }

        await using HostTestLifetime server = await this.StartWorkerAsync(
            b => b.AddTasks(
                tasks => tasks.AddOrchestratorFunc<int, int>(nameof(OrchestratorFunc), OrchestratorFunc)));

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            nameof(OrchestratorFunc),
            input: 0);

        for (int i = 0; i < EventCount; i++)
        {
            await server.Client.RaiseEventAsync(instanceId, eventName: "Event");
            await Task.Delay(TimeSpan.FromMilliseconds(1), this.TimeoutToken);
        }

        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal(EventCount, metadata.ReadOutputAs<int>());
    }

    [Fact]
    public async Task CatchingActivityExceptionsByType()
    {
        TaskName orchestratorName = nameof(CatchingActivityExceptionsByType);
        TaskName throwInvalidOpActivityName = "ThrowInvalidOp";
        TaskName throwArgumentActivityName = "ThrowArgument";
        TaskName successActivityName = "Success";

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    List<string> results = new();

                    // Test 1: Catch InvalidOperationException
                    try
                    {
                        await ctx.CallActivityAsync(throwInvalidOpActivityName);
                        results.Add("No exception thrown");
                    }
                    catch (TaskFailedException ex) when (ex.FailureDetails?.IsCausedBy<InvalidOperationException>() == true)
                    {
                        results.Add("Caught InvalidOperationException");
                    }
                    catch (TaskFailedException)
                    {
                        results.Add("Caught wrong exception type");
                    }

                    // Test 2: Catch ArgumentException
                    try
                    {
                        await ctx.CallActivityAsync(throwArgumentActivityName);
                        results.Add("No exception thrown");
                    }
                    catch (TaskFailedException ex) when (ex.FailureDetails?.IsCausedBy<ArgumentException>() == true)
                    {
                        results.Add("Caught ArgumentException");
                    }
                    catch (TaskFailedException)
                    {
                        results.Add("Caught wrong exception type");
                    }

                    // Test 3: Successful activity should not throw
                    try
                    {
                        string result = await ctx.CallActivityAsync<string>(successActivityName);
                        results.Add(result);
                    }
                    catch (TaskFailedException)
                    {
                        results.Add("Unexpected exception");
                    }

                    // Test 4: Catch with base Exception type
                    try
                    {
                        await ctx.CallActivityAsync(throwInvalidOpActivityName);
                        results.Add("No exception thrown");
                    }
                    catch (TaskFailedException ex) when (ex.FailureDetails?.IsCausedBy<Exception>() == true)
                    {
                        results.Add("Caught base Exception");
                    }

                    return results;
                })
                .AddActivityFunc(throwInvalidOpActivityName, (TaskActivityContext ctx) =>
                {
                    throw new InvalidOperationException("Invalid operation");
                })
                .AddActivityFunc(throwArgumentActivityName, (TaskActivityContext ctx) =>
                {
                    throw new ArgumentException("Invalid argument");
                })
                .AddActivityFunc<string>(successActivityName, (TaskActivityContext ctx) =>
                {
                    return "Success";
                }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);

        List<string>? results = metadata.ReadOutputAs<List<string>>();
        Assert.NotNull(results);
        Assert.Equal(4, results!.Count);
        Assert.Equal("Caught InvalidOperationException", results[0]);
        Assert.Equal("Caught ArgumentException", results[1]);
        Assert.Equal("Success", results[2]);
        Assert.Equal("Caught base Exception", results[3]);
    }

    [Fact]
    public async Task WaitForExternalEvent_WithTimeoutAndCancellationToken_EventWins()
    {
        const string EventName = "TestEvent";
        const string EventPayload = "test-payload";
        TaskName orchestratorName = nameof(WaitForExternalEvent_WithTimeoutAndCancellationToken_EventWins);

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc(orchestratorName, async ctx =>
            {
                using CancellationTokenSource cts = new();
                Task<string> eventTask = ctx.WaitForExternalEvent<string>(EventName, TimeSpan.FromDays(7), cts.Token);
                string result = await eventTask;
                return result;
            }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        await server.Client.WaitForInstanceStartAsync(instanceId, this.TimeoutToken);

        // Send event - should complete the wait
        await server.Client.RaiseEventAsync(instanceId, EventName, EventPayload);

        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);

        string? result = metadata.ReadOutputAs<string>();
        Assert.Equal(EventPayload, result);
    }

    [Fact]
    public async Task WaitForExternalEvent_WithTimeoutAndCancellationToken_CancellationWins()
    {
        TaskName orchestratorName = nameof(WaitForExternalEvent_WithTimeoutAndCancellationToken_CancellationWins);

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc(orchestratorName, async ctx =>
            {
                using CancellationTokenSource cts = new();
                
                // Create two event waiters with cancellation tokens
                Task<string> event1Task = ctx.WaitForExternalEvent<string>("Event1", TimeSpan.FromDays(7), cts.Token);
                
                using CancellationTokenSource cts2 = new();
                Task<string> event2Task = ctx.WaitForExternalEvent<string>("Event2", TimeSpan.FromDays(7), cts2.Token);

                // Wait for any to complete
                Task winner = await Task.WhenAny(event1Task, event2Task);
                
                // Cancel the other one
                if (winner == event1Task)
                {
                    cts2.Cancel();
                    return $"Event1: {await event1Task}";
                }
                else
                {
                    cts.Cancel();
                    return $"Event2: {await event2Task}";
                }
            }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        await server.Client.WaitForInstanceStartAsync(instanceId, this.TimeoutToken);

        // Send Event1 - should complete and cancel Event2
        await server.Client.RaiseEventAsync(instanceId, "Event1", "first-event");

        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);

        string? result = metadata.ReadOutputAs<string>();
        Assert.Equal("Event1: first-event", result);
    }

    [Fact]
    public async Task WaitForExternalEvent_WithTimeoutAndCancellationToken_TimeoutWins()
    {
        const string EventName = "TestEvent";
        TaskName orchestratorName = nameof(WaitForExternalEvent_WithTimeoutAndCancellationToken_TimeoutWins);

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc(orchestratorName, async ctx =>
            {
                using CancellationTokenSource cts = new();
                Task<string> eventTask = ctx.WaitForExternalEvent<string>(EventName, TimeSpan.FromMilliseconds(500), cts.Token);
                
                try
                {
                    string result = await eventTask;
                    return $"Event: {result}";
                }
                catch (OperationCanceledException)
                {
                    return "Timeout occurred";
                }
            }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);

        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);

        string? result = metadata.ReadOutputAs<string>();
        Assert.Equal("Timeout occurred", result);
    }

    [Fact]
    public async Task WaitForExternalEvent_WithTimeoutAndCancellationToken_ExternalCancellationWins()
    {
        const string EventName = "TestEvent";
        TaskName orchestratorName = nameof(WaitForExternalEvent_WithTimeoutAndCancellationToken_ExternalCancellationWins);

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc(orchestratorName, async ctx =>
            {
                using CancellationTokenSource cts = new();
                
                // Create a timer that will fire and trigger cancellation
                Task cancelTrigger = ctx.CreateTimer(TimeSpan.FromMilliseconds(100), CancellationToken.None);
                
                // Wait for external event with a long timeout
                Task<string> eventTask = ctx.WaitForExternalEvent<string>(EventName, TimeSpan.FromDays(7), cts.Token);
                
                // Wait for either the cancel trigger or the event
                Task winner = await Task.WhenAny(cancelTrigger, eventTask);
                
                if (winner == cancelTrigger)
                {
                    // Cancel the external cancellation token
                    cts.Cancel();
                }
                
                try
                {
                    string result = await eventTask;
                    return $"Event: {result}";
                }
                catch (OperationCanceledException)
                {
                    return "External cancellation occurred";
                }
            }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);

        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);

        string? result = metadata.ReadOutputAs<string>();
        Assert.Equal("External cancellation occurred", result);
    }
}
