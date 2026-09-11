// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Tests.Logging;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Microsoft.DurableTask.Grpc.Tests;

/// <summary>
/// End-to-end rewind tests that run against the DTS emulator.
/// </summary>
[Trait("Category", "DtsEmulator")]
public sealed class DtsRewindIntegrationTests : IDisposable
{
    readonly CancellationTokenSource testTimeoutSource =
        new(Debugger.IsAttached ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(60));
    readonly TestLogProvider logProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DtsRewindIntegrationTests"/> class.
    /// </summary>
    /// <param name="output">The test output helper.</param>
    public DtsRewindIntegrationTests(ITestOutputHelper output)
    {
        this.logProvider = new(output);
    }

    CancellationToken TimeoutToken => this.testTimeoutSource.Token;

    static string ConnectionString =>
        Environment.GetEnvironmentVariable(DtsEmulatorFactAttribute.ConnectionStringEnvironmentVariable)
        ?? throw new InvalidOperationException(
            $"{DtsEmulatorFactAttribute.ConnectionStringEnvironmentVariable} is not set.");

    /// <summary>
    /// Verifies that rewinding re-executes a failed activity.
    /// </summary>
    [DtsEmulatorFact]
    public async Task RewindFailedActivityAsync()
    {
        // Arrange
        TaskName orchestratorName = nameof(RewindFailedActivityAsync);
        TaskName activityName = $"{nameof(RewindFailedActivityAsync)}_Activity";
        int activityCallCount = 0;
        int shouldFail = 1;

        await using HostTestLifetime server = await this.StartWorkerAsync(builder =>
        {
            builder.AddTasks(tasks => tasks
                .AddOrchestratorFunc<string, string>(
                    orchestratorName,
                    async (context, input) =>
                        await context.CallActivityAsync<string>(activityName, input))
                .AddActivityFunc<string, string>(activityName, (context, input) =>
                {
                    Interlocked.Increment(ref activityCallCount);
                    if (Volatile.Read(ref shouldFail) == 1)
                    {
                        throw new InvalidOperationException("Simulated failure.");
                    }

                    return Task.FromResult($"Hello, {input}!");
                }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            orchestratorName,
            "World",
            cancellation: this.TimeoutToken);
        OrchestrationMetadata failed = await this.WaitForCompletionAsync(server.Client, instanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, failed.RuntimeStatus);

        // Act
        Volatile.Write(ref shouldFail, 0);
        await server.Client.RewindInstanceAsync(instanceId, "retry after fix", this.TimeoutToken);
        OrchestrationMetadata completed = await this.WaitForCompletionAsync(server.Client, instanceId);

        // Assert
        Assert.Equal(OrchestrationRuntimeStatus.Completed, completed.RuntimeStatus);
        Assert.Equal("Hello, World!", completed.ReadOutputAs<string>());
        Assert.Null(completed.FailureDetails);
        Assert.Equal(2, Volatile.Read(ref activityCallCount));
    }

    /// <summary>
    /// Verifies that rewind replays successful activity results and re-executes only the failed activity.
    /// </summary>
    [DtsEmulatorFact]
    public async Task RewindPreservesSuccessfulResultsAsync()
    {
        // Arrange
        TaskName orchestratorName = nameof(RewindPreservesSuccessfulResultsAsync);
        TaskName firstActivityName = $"{nameof(RewindPreservesSuccessfulResultsAsync)}_First";
        TaskName secondActivityName = $"{nameof(RewindPreservesSuccessfulResultsAsync)}_Second";
        ConcurrentDictionary<string, int> callCounts = [];
        int shouldFailSecond = 1;

        await using HostTestLifetime server = await this.StartWorkerAsync(builder =>
        {
            builder.AddTasks(tasks => tasks
                .AddOrchestratorFunc<string, string[]>(
                    orchestratorName,
                    async (context, input) =>
                    {
                        string first = await context.CallActivityAsync<string>(firstActivityName, input);
                        string second = await context.CallActivityAsync<string>(secondActivityName, input);
                        return [first, second];
                    })
                .AddActivityFunc<string, string>(firstActivityName, (context, input) =>
                {
                    callCounts.AddOrUpdate("first", 1, (_, count) => count + 1);
                    return Task.FromResult($"first:{input}");
                })
                .AddActivityFunc<string, string>(secondActivityName, (context, input) =>
                {
                    callCounts.AddOrUpdate("second", 1, (_, count) => count + 1);
                    if (Volatile.Read(ref shouldFailSecond) == 1)
                    {
                        throw new InvalidOperationException("Temporary failure.");
                    }

                    return Task.FromResult($"second:{input}");
                }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            orchestratorName,
            "test",
            cancellation: this.TimeoutToken);
        OrchestrationMetadata failed = await this.WaitForCompletionAsync(server.Client, instanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, failed.RuntimeStatus);

        // Act
        Volatile.Write(ref shouldFailSecond, 0);
        await server.Client.RewindInstanceAsync(instanceId, "retry", this.TimeoutToken);
        OrchestrationMetadata completed = await this.WaitForCompletionAsync(server.Client, instanceId);

        // Assert
        Assert.Equal(OrchestrationRuntimeStatus.Completed, completed.RuntimeStatus);
        string[] output = completed.ReadOutputAs<string[]>()
            ?? throw new InvalidOperationException("The orchestration output was missing.");
        Assert.Equal(["first:test", "second:test"], output);
        Assert.Null(completed.FailureDetails);
        Assert.Equal(1, callCounts["first"]);
        Assert.Equal(2, callCounts["second"]);
    }

    /// <summary>
    /// Verifies that rewinding a nonexistent orchestration fails.
    /// </summary>
    [DtsEmulatorFact]
    public async Task RewindNonexistentOrchestrationThrowsAsync()
    {
        // Arrange
        await using HostTestLifetime server = await this.StartClientAsync();
        string instanceId = $"nonexistent-{Guid.NewGuid():N}";

        // Act
        Func<Task> act = () => server.Client.RewindInstanceAsync(
            instanceId,
            "should fail",
            this.TimeoutToken);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    /// <summary>
    /// Verifies that rewinding a completed orchestration fails.
    /// </summary>
    [DtsEmulatorFact]
    public async Task RewindCompletedOrchestrationThrowsAsync()
    {
        // Arrange
        TaskName orchestratorName = nameof(RewindCompletedOrchestrationThrowsAsync);
        await using HostTestLifetime server = await this.StartWorkerAsync(builder =>
        {
            builder.AddTasks(tasks => tasks.AddOrchestratorFunc(
                orchestratorName,
                context => Task.FromResult<object?>("done")));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            orchestratorName,
            cancellation: this.TimeoutToken);
        OrchestrationMetadata completed = await this.WaitForCompletionAsync(server.Client, instanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, completed.RuntimeStatus);

        // Act
        Func<Task> act = () => server.Client.RewindInstanceAsync(
            instanceId,
            "should fail",
            this.TimeoutToken);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    /// <summary>
    /// Verifies that rewind recursively re-executes failed sub-orchestrations without restarting them.
    /// </summary>
    [DtsEmulatorFact(Skip = "Requires a DTS emulator image containing the sidecar rewind history fix.")]
    public async Task CanRewindFailedSubOrchestrationsAsync()
    {
        // Arrange
        TaskName parentOrchestratorName =
            $"{nameof(CanRewindFailedSubOrchestrationsAsync)}_Parent";
        TaskName childOrchestratorName =
            $"{nameof(CanRewindFailedSubOrchestrationsAsync)}_Child";
        TaskName activityName =
            $"{nameof(CanRewindFailedSubOrchestrationsAsync)}_Activity";
        ConcurrentDictionary<int, int> activityCallCounts = [];
        int shouldFail = 1;

        await using HostTestLifetime server = await this.StartWorkerAsync(builder =>
        {
            builder.AddTasks(tasks => tasks
                .AddOrchestratorFunc<int[]>(
                    parentOrchestratorName,
                    async context =>
                    {
                        List<Task<int>> children = [];
                        for (int childIndex = 0; childIndex < 3; childIndex++)
                        {
                            children.Add(context.CallSubOrchestratorAsync<int>(
                                childOrchestratorName,
                                childIndex,
                                new SubOrchestrationOptions(
                                    instanceId: $"{context.InstanceId}-child-{childIndex}")));
                        }

                        return await Task.WhenAll(children);
                    })
                .AddOrchestratorFunc<int, int>(
                    childOrchestratorName,
                    async (context, childIndex) =>
                        await context.CallActivityAsync<int>(activityName, childIndex))
                .AddActivityFunc<int, int>(activityName, (context, childIndex) =>
                {
                    activityCallCounts.AddOrUpdate(
                        childIndex,
                        1,
                        (_, current) => current + 1);

                    if (childIndex < 2 && Volatile.Read(ref shouldFail) == 1)
                    {
                        throw new InvalidOperationException($"Child {childIndex} failed.");
                    }

                    return Task.FromResult(childIndex);
                }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            parentOrchestratorName,
            cancellation: this.TimeoutToken);
        OrchestrationMetadata failed = await this.WaitForCompletionAsync(server.Client, instanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, failed.RuntimeStatus);
        Assert.Equal(1, activityCallCounts[0]);
        Assert.Equal(1, activityCallCounts[1]);
        Assert.Equal(1, activityCallCounts[2]);

        // Act
        Volatile.Write(ref shouldFail, 0);
        await server.Client.RewindInstanceAsync(instanceId, "retry failed children", this.TimeoutToken);
        OrchestrationMetadata completed = await this.WaitForCompletionAsync(server.Client, instanceId);
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Assert
        Assert.Equal(OrchestrationRuntimeStatus.Completed, completed.RuntimeStatus);
        int[] output = completed.ReadOutputAs<int[]>()
            ?? throw new InvalidOperationException("The parent output was missing.");
        Assert.Equal([0, 1, 2], output);
        Assert.Equal(2, activityCallCounts[0]);
        Assert.Equal(2, activityCallCounts[1]);
        Assert.Equal(1, activityCallCounts[2]);
    }

    /// <summary>
    /// Verifies that rewind recursively reaches a failed nested sub-orchestration.
    /// </summary>
    [DtsEmulatorFact]
    public async Task CanRewindNestedFailedSubOrchestrationAsync()
    {
        // Arrange
        TaskName parentOrchestratorName = $"{nameof(CanRewindNestedFailedSubOrchestrationAsync)}_Parent";
        TaskName childOrchestratorName = $"{nameof(CanRewindNestedFailedSubOrchestrationAsync)}_Child";
        TaskName nestedOrchestratorName = $"{nameof(CanRewindNestedFailedSubOrchestrationAsync)}_Nested";
        TaskName activityName = $"{nameof(CanRewindNestedFailedSubOrchestrationAsync)}_Activity";
        int activityCallCount = 0;

        await using HostTestLifetime server = await this.StartWorkerAsync(builder =>
        {
            builder.AddTasks(tasks => tasks
                .AddOrchestratorFunc<string, string>(
                    parentOrchestratorName,
                    async (context, input) =>
                    {
                        string result = await context.CallSubOrchestratorAsync<string>(
                            childOrchestratorName,
                            input,
                            new SubOrchestrationOptions(instanceId: $"{context.InstanceId}-child"));
                        return $"parent:{result}";
                    })
                .AddOrchestratorFunc<string, string>(
                    childOrchestratorName,
                    async (context, input) =>
                    {
                        string result = await context.CallSubOrchestratorAsync<string>(
                            nestedOrchestratorName,
                            input,
                            new SubOrchestrationOptions(instanceId: $"{context.InstanceId}-nested"));
                        return $"child:{result}";
                    })
                .AddOrchestratorFunc<string, string>(
                    nestedOrchestratorName,
                    async (context, input) =>
                        $"nested:{await context.CallActivityAsync<string>(activityName, input)}")
                .AddActivityFunc<string, string>(activityName, (context, input) =>
                {
                    int count = Interlocked.Increment(ref activityCallCount);
                    if (count == 1)
                    {
                        throw new InvalidOperationException("Nested child failure.");
                    }

                    return Task.FromResult($"activity:{input}");
                }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            parentOrchestratorName,
            "data",
            cancellation: this.TimeoutToken);
        OrchestrationMetadata failed = await this.WaitForCompletionAsync(server.Client, instanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, failed.RuntimeStatus);

        // Act
        await server.Client.RewindInstanceAsync(instanceId, "retry nested child", this.TimeoutToken);
        OrchestrationMetadata completed = await this.WaitForCompletionAsync(server.Client, instanceId);

        // Assert
        Assert.Equal(OrchestrationRuntimeStatus.Completed, completed.RuntimeStatus);
        Assert.Equal("parent:child:nested:activity:data", completed.ReadOutputAs<string>());
        Assert.Equal(2, Volatile.Read(ref activityCallCount));
    }

    /// <summary>
    /// Verifies that a purged failed sub-orchestration and its purged failed child are recreated on rewind.
    /// </summary>
    [DtsEmulatorFact]
    public async Task RewindPurgedNestedSubOrchestrationsAsync()
    {
        // Arrange
        TaskName parentOrchestratorName = $"{nameof(RewindPurgedNestedSubOrchestrationsAsync)}_Parent";
        TaskName childOrchestratorName = $"{nameof(RewindPurgedNestedSubOrchestrationsAsync)}_Child";
        TaskName nestedOrchestratorName = $"{nameof(RewindPurgedNestedSubOrchestrationsAsync)}_Nested";
        TaskName activityName = $"{nameof(RewindPurgedNestedSubOrchestrationsAsync)}_Activity";
        string childInstanceId = $"child-{Guid.NewGuid():N}";
        string nestedInstanceId = $"nested-{Guid.NewGuid():N}";
        const string ChildVersion = "1.0";
        const string NestedVersion = "2.0";
        SubOrchestrationOptions childOptions = new(instanceId: childInstanceId)
        {
            Version = ChildVersion,
            Tags = new Dictionary<string, string>
            {
                ["scenario"] = "purged-nested-sub-orchestration",
                ["level"] = "child",
                ["preserve"] = "true",
            },
        };
        SubOrchestrationOptions nestedOptions = new(instanceId: nestedInstanceId)
        {
            Version = NestedVersion,
            Tags = new Dictionary<string, string>
            {
                ["scenario"] = "purged-nested-sub-orchestration",
                ["level"] = "nested",
                ["preserve"] = "true",
            },
        };
        int activityCallCount = 0;

        await using HostTestLifetime server = await this.StartWorkerAsync(builder =>
        {
            builder.AddTasks(tasks => tasks
                .AddOrchestratorFunc<string, string>(
                    parentOrchestratorName,
                    async (context, input) =>
                    {
                        string result = await context.CallSubOrchestratorAsync<string>(
                            childOrchestratorName,
                            input,
                            childOptions);
                        return $"parent:{result}";
                    })
                .AddOrchestratorFunc<string, string>(
                    childOrchestratorName,
                    async (context, input) =>
                    {
                        string result = await context.CallSubOrchestratorAsync<string>(
                            nestedOrchestratorName,
                            input,
                            nestedOptions);
                        return $"{context.Version}:child:{result}";
                    })
                .AddOrchestratorFunc<string, string>(
                    nestedOrchestratorName,
                    async (context, input) =>
                        $"{context.Version}:nested:{await context.CallActivityAsync<string>(activityName, input)}")
                .AddActivityFunc<string, string>(activityName, (context, input) =>
                {
                    int count = Interlocked.Increment(ref activityCallCount);
                    if (count == 1)
                    {
                        throw new InvalidOperationException("Nested child failure.");
                    }

                    return Task.FromResult($"activity:{input}");
                }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            parentOrchestratorName,
            "data",
            cancellation: this.TimeoutToken);
        OrchestrationMetadata failed = await this.WaitForCompletionAsync(server.Client, instanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, failed.RuntimeStatus);
        Assert.Equal(1, Volatile.Read(ref activityCallCount));

        PurgeResult nestedPurgeResult = await server.Client.PurgeInstanceAsync(
            nestedInstanceId,
            this.TimeoutToken);
        Assert.Equal(1, nestedPurgeResult.PurgedInstanceCount);

        PurgeResult childPurgeResult = await server.Client.PurgeInstanceAsync(
            childInstanceId,
            this.TimeoutToken);
        Assert.Equal(1, childPurgeResult.PurgedInstanceCount);

        // Act
        await server.Client.RewindInstanceAsync(instanceId, "purge and retry nested child", this.TimeoutToken);
        OrchestrationMetadata completed = await this.WaitForCompletionAsync(server.Client, instanceId);
        await Task.Delay(TimeSpan.FromSeconds(2));

        OrchestrationMetadata recreatedChild = await server.Client.GetInstanceAsync(
            childInstanceId,
            getInputsAndOutputs: true,
            this.TimeoutToken)
            ?? throw new InvalidOperationException("The recreated child orchestration was not found.");
        OrchestrationMetadata recreatedNestedChild = await server.Client.GetInstanceAsync(
            nestedInstanceId,
            getInputsAndOutputs: true,
            this.TimeoutToken)
            ?? throw new InvalidOperationException("The recreated nested child orchestration was not found.");

        // Assert
        Assert.Equal(OrchestrationRuntimeStatus.Completed, completed.RuntimeStatus);
        Assert.Equal(
            $"parent:{ChildVersion}:child:{NestedVersion}:nested:activity:data",
            completed.ReadOutputAs<string>());
        Assert.Equal(2, Volatile.Read(ref activityCallCount));

        Assert.Equal(childOrchestratorName.Name, recreatedChild.Name);
        Assert.Equal(childInstanceId, recreatedChild.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, recreatedChild.RuntimeStatus);
        Assert.Equal("data", recreatedChild.ReadInputAs<string>());
        Assert.Equal(
            $"{ChildVersion}:child:{NestedVersion}:nested:activity:data",
            recreatedChild.ReadOutputAs<string>());
        Assert.Equal(childOptions.Tags, recreatedChild.Tags);

        Assert.Equal(nestedOrchestratorName.Name, recreatedNestedChild.Name);
        Assert.Equal(nestedInstanceId, recreatedNestedChild.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, recreatedNestedChild.RuntimeStatus);
        Assert.Equal("data", recreatedNestedChild.ReadInputAs<string>());
        Assert.Equal(
            $"{NestedVersion}:nested:activity:data",
            recreatedNestedChild.ReadOutputAs<string>());
        Assert.Equal(nestedOptions.Tags, recreatedNestedChild.Tags);
    }

    /// <summary>
    /// Verifies that purged failed sub-orchestrations are recreated when their parent is rewound.
    /// </summary>
    [DtsEmulatorFact]
    public async Task RewindPurgedSubOrchestrationsAsync()
    {
        // Arrange
        TaskName parentOrchestratorName = $"{nameof(RewindPurgedSubOrchestrationsAsync)}_Parent";
        TaskName childOrchestratorName = $"{nameof(RewindPurgedSubOrchestrationsAsync)}_Child";
        TaskName activityName = $"{nameof(RewindPurgedSubOrchestrationsAsync)}_Activity";
        const int ChildCount = 2;
        string[] childInstanceIds = Enumerable.Range(0, ChildCount)
            .Select(childIndex => $"child-{childIndex}-{Guid.NewGuid():N}")
            .ToArray();
        const string ChildVersion = "1.0";
        SubOrchestrationOptions[] childOptions = childInstanceIds
            .Select((instanceId, childIndex) => new SubOrchestrationOptions(instanceId: instanceId)
            {
                Version = ChildVersion,
                Tags = new Dictionary<string, string>
                {
                    ["scenario"] = "purged-sub-orchestration",
                    ["child-index"] = childIndex.ToString(),
                    ["preserve"] = "true",
                },
            })
            .ToArray();
        ConcurrentDictionary<string, int> activityCallCounts = [];

        await using HostTestLifetime server = await this.StartWorkerAsync(builder =>
        {
            builder.AddTasks(tasks => tasks
                .AddOrchestratorFunc<string[]>(
                    parentOrchestratorName,
                    async context =>
                    {
                        List<Task<string>> children = [];
                        for (int childIndex = 0; childIndex < childOptions.Length; childIndex++)
                        {
                            children.Add(context.CallSubOrchestratorAsync<string>(
                                childOrchestratorName,
                                $"data-{childIndex}",
                                childOptions[childIndex]));
                        }

                        return await Task.WhenAll(children);
                    })
                .AddOrchestratorFunc<string, string>(
                    childOrchestratorName,
                    async (context, input) =>
                    {
                        string result = await context.CallActivityAsync<string>(activityName, input);
                        return $"{context.Version}:{result}";
                    })
                .AddActivityFunc<string, string>(activityName, (context, input) =>
                {
                    int count = activityCallCounts.AddOrUpdate(input, 1, (_, current) => current + 1);
                    if (count == 1)
                    {
                        throw new InvalidOperationException($"Child {input} failed.");
                    }

                    return Task.FromResult($"child:{input}");
                }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            parentOrchestratorName,
            cancellation: this.TimeoutToken);
        OrchestrationMetadata failed = await this.WaitForCompletionAsync(server.Client, instanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, failed.RuntimeStatus);
        for (int childIndex = 0; childIndex < ChildCount; childIndex++)
        {
            Assert.Equal(1, activityCallCounts[$"data-{childIndex}"]);
        }

        foreach (string childInstanceId in childInstanceIds)
        {
            PurgeResult purgeResult = await server.Client.PurgeInstanceAsync(
                childInstanceId,
                this.TimeoutToken);
            Assert.Equal(1, purgeResult.PurgedInstanceCount);
        }

        // Act
        await server.Client.RewindInstanceAsync(instanceId, "purge and retry", this.TimeoutToken);
        OrchestrationMetadata completed = await this.WaitForCompletionAsync(server.Client, instanceId);
        await Task.Delay(TimeSpan.FromSeconds(2));
        OrchestrationMetadata?[] recreatedChildren = new OrchestrationMetadata?[childInstanceIds.Length];
        for (int childIndex = 0; childIndex < childInstanceIds.Length; childIndex++)
        {
            recreatedChildren[childIndex] = await server.Client.GetInstanceAsync(
                childInstanceIds[childIndex],
                getInputsAndOutputs: true,
                this.TimeoutToken);
        }

        // Assert
        Assert.Equal(OrchestrationRuntimeStatus.Completed, completed.RuntimeStatus);
        string[] output = completed.ReadOutputAs<string[]>()
            ?? throw new InvalidOperationException("The parent output was missing.");
        Assert.Equal(
            Enumerable.Range(0, ChildCount)
                .Select(childIndex => $"{ChildVersion}:child:data-{childIndex}")
                .ToArray(),
            output);
        for (int childIndex = 0; childIndex < ChildCount; childIndex++)
        {
            Assert.Equal(2, activityCallCounts[$"data-{childIndex}"]);
        }

        for (int childIndex = 0; childIndex < childInstanceIds.Length; childIndex++)
        {
            OrchestrationMetadata recreatedChild = recreatedChildren[childIndex]
                ?? throw new InvalidOperationException(
                    $"The recreated child orchestration {childIndex} was not found.");
            Assert.Equal(childOrchestratorName.Name, recreatedChild.Name);
            Assert.Equal(childInstanceIds[childIndex], recreatedChild.InstanceId);
            Assert.Equal(OrchestrationRuntimeStatus.Completed, recreatedChild.RuntimeStatus);
            Assert.Equal($"data-{childIndex}", recreatedChild.ReadInputAs<string>());
            Assert.Equal(
                $"{ChildVersion}:child:data-{childIndex}",
                recreatedChild.ReadOutputAs<string>());
            Assert.Equal(childOptions[childIndex].Tags, recreatedChild.Tags);
        }
    }

    /// <summary>
    /// Verifies that rewind accepts an empty reason.
    /// </summary>
    [DtsEmulatorFact]
    public async Task RewindWithoutReasonAsync()
    {
        // Arrange
        TaskName orchestratorName = nameof(RewindWithoutReasonAsync);
        TaskName activityName = $"{nameof(RewindWithoutReasonAsync)}_Activity";
        int activityCallCount = 0;

        await using HostTestLifetime server = await this.StartWorkerAsync(builder =>
        {
            builder.AddTasks(tasks => tasks
                .AddOrchestratorFunc(
                    orchestratorName,
                    async context => await context.CallActivityAsync<string>(activityName))
                .AddActivityFunc(activityName, (TaskActivityContext context) =>
                {
                    int count = Interlocked.Increment(ref activityCallCount);
                    if (count == 1)
                    {
                        throw new InvalidOperationException("Simulated failure.");
                    }

                    return Task.FromResult<object?>("ok");
                }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            orchestratorName,
            cancellation: this.TimeoutToken);
        OrchestrationMetadata failed = await this.WaitForCompletionAsync(server.Client, instanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, failed.RuntimeStatus);

        // Act
        await server.Client.RewindInstanceAsync(instanceId, string.Empty, this.TimeoutToken);
        OrchestrationMetadata completed = await this.WaitForCompletionAsync(server.Client, instanceId);

        // Assert
        Assert.Equal(OrchestrationRuntimeStatus.Completed, completed.RuntimeStatus);
        Assert.Equal("ok", completed.ReadOutputAs<string>());
    }

    /// <summary>
    /// Verifies that the same orchestration can be rewound more than once.
    /// </summary>
    [DtsEmulatorFact]
    public async Task RewindTwiceAsync()
    {
        // Arrange
        TaskName orchestratorName = nameof(RewindTwiceAsync);
        TaskName activityName = $"{nameof(RewindTwiceAsync)}_Activity";
        int activityCallCount = 0;

        await using HostTestLifetime server = await this.StartWorkerAsync(builder =>
        {
            builder.AddTasks(tasks => tasks
                .AddOrchestratorFunc<string, string>(
                    orchestratorName,
                    async (context, input) =>
                        await context.CallActivityAsync<string>(activityName, input))
                .AddActivityFunc<string, string>(activityName, (context, input) =>
                {
                    int count = Interlocked.Increment(ref activityCallCount);
                    if (count <= 2)
                    {
                        throw new InvalidOperationException($"Failure #{count}.");
                    }

                    return Task.FromResult($"Hello, {input}!");
                }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            orchestratorName,
            "World",
            cancellation: this.TimeoutToken);
        OrchestrationMetadata firstFailure = await this.WaitForCompletionAsync(server.Client, instanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, firstFailure.RuntimeStatus);

        await server.Client.RewindInstanceAsync(instanceId, "first rewind", this.TimeoutToken);
        OrchestrationMetadata secondFailure = await this.WaitForCompletionAsync(server.Client, instanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, secondFailure.RuntimeStatus);

        // Act
        await server.Client.RewindInstanceAsync(instanceId, "second rewind", this.TimeoutToken);
        OrchestrationMetadata completed = await this.WaitForCompletionAsync(server.Client, instanceId);

        // Assert
        Assert.Equal(OrchestrationRuntimeStatus.Completed, completed.RuntimeStatus);
        Assert.Equal("Hello, World!", completed.ReadOutputAs<string>());
        Assert.Equal(3, Volatile.Read(ref activityCallCount));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        this.testTimeoutSource.Dispose();
    }

    async Task<HostTestLifetime> StartClientAsync()
    {
        return await this.StartHostAsync(configureWorker: null);
    }

    async Task<HostTestLifetime> StartWorkerAsync(Action<IDurableTaskWorkerBuilder> configureWorker)
    {
        return await this.StartHostAsync(configureWorker);
    }

    async Task<HostTestLifetime> StartHostAsync(Action<IDurableTaskWorkerBuilder>? configureWorker)
    {
        IHost host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(this.logProvider);
                logging.SetMinimumLevel(LogLevel.Warning);
            })
            .ConfigureServices(services =>
            {
                if (configureWorker is not null)
                {
                    services.AddDurableTaskWorker(builder =>
                    {
                        configureWorker(builder);
                        builder.UseDurableTaskScheduler(ConnectionString);
                    });
                }

                services.AddDurableTaskClient(builder =>
                    builder.UseDurableTaskScheduler(ConnectionString));
            })
            .Build();

        try
        {
            await host.StartAsync(this.TimeoutToken);
            return new HostTestLifetime(host, this.TimeoutToken);
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    async Task<OrchestrationMetadata> WaitForCompletionAsync(
        DurableTaskClient client,
        string instanceId)
    {
        return await client.WaitForInstanceCompletionAsync(
            instanceId,
            getInputsAndOutputs: true,
            this.TimeoutToken);
    }

    sealed class HostTestLifetime : IAsyncDisposable
    {
        readonly IHost host;
        readonly CancellationToken cancellation;

        public HostTestLifetime(IHost host, CancellationToken cancellation)
        {
            this.host = host;
            this.cancellation = cancellation;
            this.Client = host.Services.GetRequiredService<DurableTaskClient>();
        }

        public DurableTaskClient Client { get; }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await this.host.StopAsync(this.cancellation);
            }
            finally
            {
                this.host.Dispose();
            }
        }
    }
}
