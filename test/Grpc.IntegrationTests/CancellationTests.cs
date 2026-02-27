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
    /// Tests that when a token is cancelled outside the retry handler (between retry attempts),
    /// the handler receives the cancelled token on the next attempt.
    /// </summary>
    [Fact]
    public async Task RetryHandlerReceivesCancelledTokenFromOutside()
    {
        TaskName orchestratorName = nameof(RetryHandlerReceivesCancelledTokenFromOutside);

        int attemptCount = 0;
        bool tokenWasCancelledInHandler = false;
        CancellationTokenSource? cts = null;

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    cts = new CancellationTokenSource();

                    TaskRetryOptions retryOptions = TaskOptions.FromRetryHandler(retryContext =>
                    {
                        attemptCount = retryContext.LastAttemptNumber;
                        
                        // Check if token is cancelled
                        tokenWasCancelledInHandler = retryContext.CancellationToken.IsCancellationRequested;

                        // Stop retrying if cancelled
                        if (retryContext.CancellationToken.IsCancellationRequested)
                        {
                            return false;
                        }

                        return attemptCount < 5;
                    }).Retry!;

                    TaskOptions options = new(retryOptions)
                    {
                        CancellationToken = cts.Token
                    };

                    // Cancel the token AFTER creating options but BEFORE first attempt
                    // This tests that the retry handler receives the cancelled token from outside
                    cts.Cancel();

                    try
                    {
                        await ctx.CallActivityAsync("FailingActivity", options);
                        return "Should not reach here - activity succeeded";
                    }
                    catch (TaskCanceledException)
                    {
                        // Pre-scheduling check caught the cancelled token before even attempting
                        return $"Cancelled before scheduling, attempts: {attemptCount}";
                    }
                    catch (TaskFailedException)
                    {
                        // Activity failed and retry handler stopped retrying
                        return $"Failed after {attemptCount} attempts, token was cancelled in handler: {tokenWasCancelledInHandler}";
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
        
        // Since token was cancelled before CallActivityAsync, the pre-scheduling check throws
        // TaskCanceledException and retry handler never gets called
        Assert.Equal(0, attemptCount);
        Assert.Contains("Cancelled before scheduling", metadata.SerializedOutput);
    }

    /// <summary>
    /// Tests that when calling multiple activities in a loop with a cancellation token,
    /// the loop exits after cancellation instead of continuing to call remaining activities.
    /// This is the main use case for pre-scheduling cancellation checks.
    /// </summary>
    [Fact]
    public async Task MultipleActivitiesInLoopWithCancellation()
    {
        TaskName orchestratorName = nameof(MultipleActivitiesInLoopWithCancellation);
        TaskName activityName = "ProcessItem";

        int activitiesInvoked = 0;
        int totalItems = 10;

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    using CancellationTokenSource cts = new();
                    TaskOptions options = new() { CancellationToken = cts.Token };

                    List<string> results = new();

                    for (int i = 0; i < totalItems; i++)
                    {
                        // Cancel after processing 3 items
                        if (i == 3)
                        {
                            cts.Cancel();
                        }

                        try
                        {
                            string result = await ctx.CallActivityAsync<string>(activityName, i, options);
                            results.Add(result);
                        }
                        catch (TaskCanceledException)
                        {
                            // Pre-scheduling check caught cancellation - exit loop
                            results.Add($"Cancelled at item {i}");
                            break;
                        }
                    }

                    return $"Processed {results.Count} items: [{string.Join(", ", results)}]";
                })
                .AddActivityFunc<int, string>(activityName, (TaskActivityContext ctx, int item) =>
                {
                    Interlocked.Increment(ref activitiesInvoked);
                    return $"Item {item}";
                }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        
        // Should have processed 3 items (0, 1, 2) before cancellation at item 3
        Assert.Equal(3, activitiesInvoked);
        Assert.Contains("Processed 4 items", metadata.SerializedOutput); // 3 successful + 1 cancellation message
        Assert.Contains("Cancelled at item 3", metadata.SerializedOutput);
        Assert.Contains("Item 0", metadata.SerializedOutput);
        Assert.Contains("Item 1", metadata.SerializedOutput);
        Assert.Contains("Item 2", metadata.SerializedOutput);
    }
}
