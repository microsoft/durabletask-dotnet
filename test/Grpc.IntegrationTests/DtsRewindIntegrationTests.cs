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
    /// Verifies that rewind recursively re-executes a failed sub-orchestration.
    /// </summary>
    [DtsEmulatorFact]
    public async Task RewindFailedSubOrchestrationAsync()
    {
        // Arrange
        TaskName parentOrchestratorName = $"{nameof(RewindFailedSubOrchestrationAsync)}_Parent";
        TaskName childOrchestratorName = $"{nameof(RewindFailedSubOrchestrationAsync)}_Child";
        TaskName activityName = $"{nameof(RewindFailedSubOrchestrationAsync)}_Activity";
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
                            input);
                        return $"parent:{result}";
                    })
                .AddOrchestratorFunc<string, string>(
                    childOrchestratorName,
                    async (context, input) =>
                        await context.CallActivityAsync<string>(activityName, input))
                .AddActivityFunc<string, string>(activityName, (context, input) =>
                {
                    int count = Interlocked.Increment(ref activityCallCount);
                    if (count == 1)
                    {
                        throw new InvalidOperationException("Child failure.");
                    }

                    return Task.FromResult($"child:{input}");
                }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            parentOrchestratorName,
            "data",
            cancellation: this.TimeoutToken);
        OrchestrationMetadata failed = await this.WaitForCompletionAsync(server.Client, instanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, failed.RuntimeStatus);

        // Act
        await server.Client.RewindInstanceAsync(instanceId, "sub-orchestration fix", this.TimeoutToken);
        OrchestrationMetadata completed = await this.WaitForCompletionAsync(server.Client, instanceId);

        // Assert
        Assert.Equal(OrchestrationRuntimeStatus.Completed, completed.RuntimeStatus);
        Assert.Equal("parent:child:data", completed.ReadOutputAs<string>());
        Assert.Equal(2, Volatile.Read(ref activityCallCount));
    }

    /// <summary>
    /// Verifies that a purged failed sub-orchestration is recreated when its parent is rewound.
    /// </summary>
    [DtsEmulatorFact]
    public async Task RewindPurgedSubOrchestrationAsync()
    {
        // Arrange
        TaskName parentOrchestratorName = $"{nameof(RewindPurgedSubOrchestrationAsync)}_Parent";
        TaskName childOrchestratorName = $"{nameof(RewindPurgedSubOrchestrationAsync)}_Child";
        TaskName activityName = $"{nameof(RewindPurgedSubOrchestrationAsync)}_Activity";
        string childInstanceId = $"child-{Guid.NewGuid():N}";
        const string ChildVersion = "v1";
        SubOrchestrationOptions childOptions = new(instanceId: childInstanceId)
        {
            Version = ChildVersion,
            Tags = new Dictionary<string, string>
            {
                ["scenario"] = "purged-sub-orchestration",
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
                        string result = await context.CallActivityAsync<string>(activityName, input);
                        return $"{context.Version}:{result}";
                    })
                .AddActivityFunc<string, string>(activityName, (context, input) =>
                {
                    int count = Interlocked.Increment(ref activityCallCount);
                    if (count == 1)
                    {
                        throw new InvalidOperationException("Child failure.");
                    }

                    return Task.FromResult($"child:{input}");
                }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            parentOrchestratorName,
            "data",
            cancellation: this.TimeoutToken);
        OrchestrationMetadata failed = await this.WaitForCompletionAsync(server.Client, instanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, failed.RuntimeStatus);

        PurgeResult purgeResult = await server.Client.PurgeInstanceAsync(
            childInstanceId,
            this.TimeoutToken);
        Assert.Equal(1, purgeResult.PurgedInstanceCount);

        // Act
        await server.Client.RewindInstanceAsync(instanceId, "purge and retry", this.TimeoutToken);
        OrchestrationMetadata completed = await this.WaitForCompletionAsync(server.Client, instanceId);

        // Assert
        Assert.Equal(OrchestrationRuntimeStatus.Completed, completed.RuntimeStatus);
        Assert.Equal($"parent:{ChildVersion}:child:data", completed.ReadOutputAs<string>());
        Assert.Equal(2, Volatile.Read(ref activityCallCount));

        OrchestrationMetadata recreatedChild = await server.Client.GetInstanceAsync(
            childInstanceId,
            getInputsAndOutputs: true,
            this.TimeoutToken)
            ?? throw new InvalidOperationException("The recreated child orchestration was not found.");
        Assert.Equal(childOrchestratorName.Name, recreatedChild.Name);
        Assert.Equal(childInstanceId, recreatedChild.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, recreatedChild.RuntimeStatus);
        Assert.Equal("data", recreatedChild.ReadInputAs<string>());
        Assert.Equal($"{ChildVersion}:child:data", recreatedChild.ReadOutputAs<string>());
        Assert.Equal(childOptions.Tags, recreatedChild.Tags);
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
