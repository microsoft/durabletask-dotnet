// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Tests.Logging;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc;
using Xunit.Abstractions;

namespace Microsoft.DurableTask.Grpc.Tests;

/// <summary>
/// Integration tests for validating autochunk functionality when orchestration completion responses
/// exceed the maximum chunk size and are automatically split into multiple chunks.
/// </summary>
public class AutochunkTests(ITestOutputHelper output, GrpcSidecarFixture sidecarFixture) : IntegrationTestBase(output, sidecarFixture)
{
    /// <summary>
    /// Validates that orchestrations complete successfully when the completion response
    /// exceeds the chunk size and must be split into multiple chunks.
    /// </summary>
    [Fact]
    public async Task Autochunk_MultipleChunks_CompletesSuccessfully()
    {
        const int ActivityCount = 36;
        const int PayloadSizePerActivity = 30 * 1024;
        const int ChunkSize = GrpcDurableTaskWorkerOptions.MinCompleteOrchestrationWorkItemChunkSizeInBytes; // 1 MB (minimum allowed)
        TaskName orchestratorName = nameof(Autochunk_MultipleChunks_CompletesSuccessfully);
        TaskName activityName = "Echo";

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            // Set a small chunk size to force chunking
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

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, cts.Token);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal(ActivityCount, metadata.ReadOutputAs<int>());
    }

    /// <summary>
    /// Validates autochunking with mixed action types (activities, timers, sub-orchestrations).
    /// </summary>
    [Fact]
    public async Task Autochunk_MixedActions_CompletesSuccessfully()
    {
        // Use minimum allowed chunk size (1 MB) and ensure total payload exceeds it to trigger chunking
        const int ActivityCount = 30;
        const int TimerCount = 100;
        const int SubOrchCount = 50;
        const int PayloadSizePerActivity = 20 * 1024;
        const int ChunkSize = GrpcDurableTaskWorkerOptions.MinCompleteOrchestrationWorkItemChunkSizeInBytes; // 1 MB (minimum allowed)
        TaskName orchestratorName = nameof(Autochunk_MixedActions_CompletesSuccessfully);
        TaskName activityName = "Echo";
        TaskName subOrchName = "SubOrch";

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            // Set a small chunk size to force chunking
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

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, cts.Token);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal(ActivityCount + TimerCount + SubOrchCount, metadata.ReadOutputAs<int>());
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

