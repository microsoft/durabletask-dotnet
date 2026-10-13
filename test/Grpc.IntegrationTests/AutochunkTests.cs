// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Tests.Logging;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc;
using Xunit.Abstractions;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Grpc.Tests;

/// <summary>
/// Integration tests for whole orchestration completion responses and single-action size validation.
/// </summary>
public class AutochunkTests(ITestOutputHelper output, GrpcSidecarFixture sidecarFixture) : IntegrationTestBase(output, sidecarFixture)
{
    readonly P.TaskHubSidecarService.TaskHubSidecarServiceClient sidecarClient = new(sidecarFixture.Channel);

    /// <summary>
    /// Validates that multiple activity actions complete in one response below the configured size limit.
    /// </summary>
    [Fact]
    public async Task FullResponse_MultipleActions_CompletesSuccessfullyAsync()
    {
        // Arrange
        const int ActivityCount = 16;
        const int PayloadSizePerActivity = 30 * 1024;
        const int ChunkSize = GrpcDurableTaskWorkerOptions.MinCompleteOrchestrationWorkItemChunkSizeInBytes; // 1 MB (minimum allowed)
        TaskName orchestratorName = nameof(FullResponse_MultipleActions_CompletesSuccessfullyAsync);
        TaskName activityName = "Echo";

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            // Keep the response below the minimum configured chunk size.
            b.UseGrpc(opt => opt.CompleteOrchestrationWorkItemChunkSizeInBytes = ChunkSize);
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    // Start all activities in parallel so they're all in the same completion response
                    List<Task<string>> tasks = new List<Task<string>>();
                    for (int i = 0; i < ActivityCount; i++)
                    {
                        string payload = new string((char)('A' + (i % 26)), PayloadSizePerActivity);
                        tasks.Add(ctx.CallActivityAsync<string>(activityName, payload));
                    }
                    string[] results = await Task.WhenAll(tasks);
                    return results.Length;
                })
                .AddActivityFunc<string, string>(activityName, (ctx, input) => Task.FromResult(input)));
        });

        // Act
        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, cts.Token);

        // Assert
        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal(ActivityCount, metadata.ReadOutputAs<int>());
    }

    /// <summary>
    /// Validates a full response with mixed action types (activities, timers, sub-orchestrations).
    /// </summary>
    [Fact]
    public async Task FullResponse_MixedActions_CompletesSuccessfullyAsync()
    {
        // Arrange
        const int ActivityCount = 30;
        const int TimerCount = 100;
        const int SubOrchCount = 50;
        const int PayloadSizePerActivity = 20 * 1024;
        const int ChunkSize = GrpcDurableTaskWorkerOptions.MinCompleteOrchestrationWorkItemChunkSizeInBytes; // 1 MB (minimum allowed)
        TaskName orchestratorName = nameof(FullResponse_MixedActions_CompletesSuccessfullyAsync);
        TaskName activityName = "Echo";
        TaskName subOrchName = "SubOrch";

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            // The mixed actions fit in a single response below the minimum configured chunk size.
            b.UseGrpc(opt => opt.CompleteOrchestrationWorkItemChunkSizeInBytes = ChunkSize);
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    // Start all actions in parallel so they're all in the same completion response
                    List<Task> allTasks = new List<Task>();

                    // Activities
                    for (int i = 0; i < ActivityCount; i++)
                    {
                        string payload = new string('A', PayloadSizePerActivity);
                        allTasks.Add(ctx.CallActivityAsync<string>(activityName, payload));
                    }

                    // Timers
                    for (int i = 0; i < TimerCount; i++)
                    {
                        allTasks.Add(ctx.CreateTimer(TimeSpan.FromMilliseconds(10), CancellationToken.None));
                    }

                    // Sub-orchestrations
                    for (int i = 0; i < SubOrchCount; i++)
                    {
                        allTasks.Add(ctx.CallSubOrchestratorAsync<int>(subOrchName, i));
                    }

                    await Task.WhenAll(allTasks);
                    return ActivityCount + TimerCount + SubOrchCount;
                })
                .AddOrchestratorFunc<int, int>(subOrchName, (ctx, input) => Task.FromResult(input))
                .AddActivityFunc<string, string>(activityName, (ctx, input) => Task.FromResult(input)));
        });

        // Act
        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, cts.Token);

        // Assert
        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal(ActivityCount + TimerCount + SubOrchCount, metadata.ReadOutputAs<int>());
    }

    /// <summary>
    /// Unsupported SDK chunks fail the orchestration instead of leaving its delivery pending.
    /// </summary>
    [Fact]
    public async Task ChunkedResponse_FailsWithoutSchedulingFragmentActionsAsync()
    {
        // Arrange
        const int ActivityCount = 36;
        const int PayloadSize = 30 * 1024;
        TaskName orchestratorName = nameof(ChunkedResponse_FailsWithoutSchedulingFragmentActionsAsync);
        TaskName activityName = "MustNotRun";
        int activitiesRun = 0;
        await using HostTestLifetime server = await this.StartWorkerAsync(builder =>
        {
            builder.UseGrpc(options => options.CompleteOrchestrationWorkItemChunkSizeInBytes =
                GrpcDurableTaskWorkerOptions.MinCompleteOrchestrationWorkItemChunkSizeInBytes);
            builder.AddTasks(tasks => tasks
                .AddOrchestratorFunc(orchestratorName, async context =>
                {
                    Task<int>[] activities = Enumerable.Range(0, ActivityCount)
                        .Select(_ => context.CallActivityAsync<int>(activityName, new string('A', PayloadSize)))
                        .ToArray();
                    return (await Task.WhenAll(activities)).Length;
                })
                .AddActivityFunc<string, int>(activityName, (_, _) =>
                {
                    Interlocked.Increment(ref activitiesRun);
                    return Task.FromResult(1);
                }));
        });
        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);

        try
        {
            // Act
            using CancellationTokenSource completionTimeout = new(TimeSpan.FromSeconds(10));
            OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
                instanceId, getInputsAndOutputs: true, completionTimeout.Token);

            // Assert
            Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);
            Assert.NotNull(metadata.FailureDetails);
            Assert.Equal(typeof(NotSupportedException).FullName, metadata.FailureDetails.ErrorType);
            Assert.Contains("Chunked orchestrator completion is not supported", metadata.FailureDetails.ErrorMessage);
            Assert.Contains("single full response", metadata.FailureDetails.ErrorMessage);
            Assert.Equal(0, Volatile.Read(ref activitiesRun));
        }
        finally
        {
            // Release the old implementation's pending delivery so a red regression cannot strand the fixture.
            foreach (LogEntry log in this.GetLogs().Where(entry => entry.EventId.Name == "AbandoningOrchestratorWorkItem"))
            {
                if (log.State is not IReadOnlyCollection<KeyValuePair<string, object>> state)
                {
                    continue;
                }

                Dictionary<string, object> metadata = new(state);
                if (metadata.GetValueOrDefault("instanceId") as string != instanceId ||
                    metadata.GetValueOrDefault("completionToken") is not string token)
                {
                    continue;
                }

                try
                {
                    await this.sidecarClient.CompleteOrchestratorTaskAsync(new()
                    {
                        InstanceId = instanceId,
                        CompletionToken = token,
                        Actions = { new P.OrchestratorAction
                        {
                            CompleteOrchestration = new() { OrchestrationStatus = P.OrchestrationStatus.Failed },
                        } },
                    }).ResponseAsync.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (RpcException exception) when (exception.StatusCode == StatusCode.NotFound)
                {
                    // The fixed implementation has already settled this delivery.
                }
            }
        }
    }

    /// <summary>
    /// Validates that when a single orchestrator action exceeds the CompleteOrchestrationWorkItemChunkSizeInBytes limit,
    /// the orchestration completes with a failed status and proper failure details.
    /// </summary>
    [Fact]
    public async Task Autochunk_SingleActionExceedsChunkSize_CompletesWithFailedStatus()
    {
        const int ChunkSize = GrpcDurableTaskWorkerOptions.MinCompleteOrchestrationWorkItemChunkSizeInBytes; // 1 MB
        // Create a payload that exceeds the chunk size (1 MB + some overhead)
        const int PayloadSize = ChunkSize + 100 * 1024; // 1.1 MB payload
        TaskName orchestratorName = nameof(Autochunk_SingleActionExceedsChunkSize_CompletesWithFailedStatus);
        TaskName activityName = "Echo";

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.UseGrpc(opt => opt.CompleteOrchestrationWorkItemChunkSizeInBytes = ChunkSize);
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc(orchestratorName, async ctx =>
                {
                    // Attempt to schedule an activity with a payload that exceeds the chunk size
                    string largePayload = new string('A', PayloadSize);
                    return await ctx.CallActivityAsync<string>(activityName, largePayload);
                })
                .AddActivityFunc<string, string>(activityName, (ctx, input) => Task.FromResult(input)));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, cts.Token);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);
        Assert.NotNull(metadata.FailureDetails);
        Assert.Equal("System.InvalidOperationException: A single orchestrator action of type ScheduleTask with id 0 exceeds the 1.00MB limit: 1.10MB. Enable large-payload externalization to Azure Blob Storage to support oversized actions.", metadata.FailureDetails.ToString());
    }
}
