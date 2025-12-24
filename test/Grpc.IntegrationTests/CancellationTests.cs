// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Tests.Logging;
using Microsoft.DurableTask.Worker;
using Xunit.Abstractions;

namespace Microsoft.DurableTask.Grpc.Tests;

/// <summary>
/// Integration tests for activity and sub-orchestrator cancellation functionality.
/// </summary>
public class CancellationTests(ITestOutputHelper output, GrpcSidecarFixture sidecarFixture) :
    IntegrationTestBase(output, sidecarFixture)
{
    /// <summary>
    /// Tests that an activity can be cancelled using a CancellationToken.
    /// </summary>
    [Fact]
    public async Task ActivityCancellation()
    {
        TaskName orchestratorName = nameof(ActivityCancellation);
        TaskName activityName = "SlowActivity";

        bool activityWasInvoked = false;

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    using CancellationTokenSource cts = new();

                    // Cancel immediately
                    cts.Cancel();

                    TaskOptions options = new() { CancellationToken = cts.Token };

                    try
                    {
                        await ctx.CallActivityAsync(activityName, options);
                        return "Should not reach here";
                    }
                    catch (TaskCanceledException)
                    {
                        return "Cancelled";
                    }
                })
                .AddActivityFunc(activityName, (TaskActivityContext activityContext) =>
                {
                    activityWasInvoked = true;
                    return "Activity completed";
                }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("\"Cancelled\"", metadata.SerializedOutput);
        Assert.False(activityWasInvoked, "Activity should not have been invoked when cancellation happens before scheduling");
    }

    /// <summary>
    /// Tests that a sub-orchestrator can be cancelled using a CancellationToken.
    /// </summary>
    [Fact]
    public async Task SubOrchestratorCancellation()
    {
        TaskName orchestratorName = nameof(SubOrchestratorCancellation);
        TaskName subOrchestratorName = "SubOrchestrator";

        bool subOrchestratorWasInvoked = false;

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    using CancellationTokenSource cts = new();

                    // Cancel immediately
                    cts.Cancel();

                    TaskOptions options = new() { CancellationToken = cts.Token };

                    try
                    {
                        await ctx.CallSubOrchestratorAsync(subOrchestratorName, options: options);
                        return "Should not reach here";
                    }
                    catch (TaskCanceledException)
                    {
                        return "Cancelled";
                    }
                })
                .AddOrchestratorFunc(subOrchestratorName, ctx =>
                {
                    subOrchestratorWasInvoked = true;
                    return Task.FromResult("Sub-orchestrator completed");
                }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("\"Cancelled\"", metadata.SerializedOutput);
        Assert.False(subOrchestratorWasInvoked, "Sub-orchestrator should not have been invoked when cancellation happens before scheduling");
    }

    /// <summary>
    /// Tests that cancellation token is passed to retry handler.
    /// </summary>
    [Fact]
    public async Task RetryHandlerReceivesCancellationToken()
    {
        TaskName orchestratorName = nameof(RetryHandlerReceivesCancellationToken);

        int attemptCount = 0;
        bool cancellationTokenWasCancelled = false;

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    using CancellationTokenSource cts = new();

                    TaskRetryOptions retryOptions = TaskOptions.FromRetryHandler(retryContext =>
                    {
                        attemptCount = retryContext.LastAttemptNumber;
                        cancellationTokenWasCancelled = retryContext.CancellationToken.IsCancellationRequested;

                        // Cancel after first attempt
                        if (attemptCount == 1)
                        {
                            cts.Cancel();
                        }

                        // Try to retry
                        return attemptCount < 5;
                    }).Retry!;

                    TaskOptions options = new(retryOptions)
                    {
                        CancellationToken = cts.Token
                    };

                    try
                    {
                        await ctx.CallActivityAsync("FailingActivity", options);
                        return "Should not reach here";
                    }
                    catch (TaskFailedException)
                    {
                        return $"Failed after {attemptCount} attempts";
                    }
                })
                .AddActivityFunc("FailingActivity", (TaskActivityContext activityContext) =>
                {
                    throw new InvalidOperationException("Activity always fails");
                }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.True(attemptCount >= 1, "Retry handler should have been called at least once");
        Assert.True(cancellationTokenWasCancelled, "Cancellation token should have been cancelled in retry handler");
    }

    /// <summary>
    /// Tests that retry handler can check cancellation token and stop retrying.
    /// </summary>
    [Fact]
    public async Task RetryHandlerCanStopOnCancellation()
    {
        TaskName orchestratorName = nameof(RetryHandlerCanStopOnCancellation);

        int maxAttempts = 0;

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    using CancellationTokenSource cts = new();

                    TaskRetryOptions retryOptions = TaskOptions.FromRetryHandler(retryContext =>
                    {
                        maxAttempts = retryContext.LastAttemptNumber;

                        // Cancel after second attempt
                        if (maxAttempts == 2)
                        {
                            cts.Cancel();
                        }

                        // Stop retrying if cancelled
                        if (retryContext.CancellationToken.IsCancellationRequested)
                        {
                            return false;
                        }

                        return maxAttempts < 10;
                    }).Retry!;

                    TaskOptions options = new(retryOptions)
                    {
                        CancellationToken = cts.Token
                    };

                    try
                    {
                        await ctx.CallActivityAsync("FailingActivity", options);
                        return "Should not reach here";
                    }
                    catch (TaskFailedException)
                    {
                        return $"Stopped after {maxAttempts} attempts";
                    }
                })
                .AddActivityFunc("FailingActivity", (TaskActivityContext activityContext) =>
                {
                    throw new InvalidOperationException("Activity always fails");
                }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal(2, maxAttempts); // Should stop after 2 attempts due to cancellation
        Assert.Equal("\"Stopped after 2 attempts\"", metadata.SerializedOutput);
    }

    /// <summary>
    /// Tests that activity can be cancelled while waiting for it to complete.
    /// </summary>
    [Fact]
    public async Task ActivityCancellationWhileWaiting()
    {
        TaskName orchestratorName = nameof(ActivityCancellationWhileWaiting);
        TaskName activityName = "LongRunningActivity";

        bool activityCompleted = false;

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(100));

                    TaskOptions options = new() { CancellationToken = cts.Token };

                    try
                    {
                        // This will start the activity, but then cancel while waiting
                        await ctx.CallActivityAsync(activityName, options);
                        return "Should not reach here";
                    }
                    catch (TaskCanceledException)
                    {
                        return "Cancelled while waiting";
                    }
                })
                .AddActivityFunc(activityName, async (TaskActivityContext activityContext) =>
                {
                    // Simulate long-running activity
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    activityCompleted = true;
                    return "Activity completed";
                }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("\"Cancelled while waiting\"", metadata.SerializedOutput);

        // Note: The activity might still complete in the background, but the orchestrator
        // should have already moved on after cancellation
    }

    /// <summary>
    /// Tests that sub-orchestrator can be cancelled while waiting for it to complete.
    /// </summary>
    [Fact]
    public async Task SubOrchestratorCancellationWhileWaiting()
    {
        TaskName orchestratorName = nameof(SubOrchestratorCancellationWhileWaiting);
        TaskName subOrchestratorName = "LongRunningSubOrchestrator";

        bool subOrchestratorCompleted = false;

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(100));

                    TaskOptions options = new() { CancellationToken = cts.Token };

                    try
                    {
                        // This will start the sub-orchestrator, but then cancel while waiting
                        await ctx.CallSubOrchestratorAsync(subOrchestratorName, options: options);
                        return "Should not reach here";
                    }
                    catch (TaskCanceledException)
                    {
                        return "Cancelled while waiting";
                    }
                })
                .AddOrchestratorFunc(subOrchestratorName, async ctx =>
                {
                    // Simulate long-running sub-orchestrator
                    await ctx.CreateTimer(TimeSpan.FromSeconds(5), CancellationToken.None);
                    subOrchestratorCompleted = true;
                    return "Sub-orchestrator completed";
                }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("\"Cancelled while waiting\"", metadata.SerializedOutput);

        // Note: The sub-orchestrator might still complete in the background, but the parent
        // orchestrator should have already moved on after cancellation
    }
}
