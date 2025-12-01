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
        // Use minimum allowed chunk size (1 MB) and ensure total payload exceeds it to trigger chunking
        // 360 activities × 3KB = ~1.05 MB, exceeding 1 MB chunk size while completing within timeout
        const int ActivityCount = 360;
        const int PayloadSizePerActivity = 3 * 1024; // 3KB per activity
        const int ChunkSize = GrpcDurableTaskWorkerOptions.MinCompleteOrchestrationWorkItemSizePerChunkBytes; // 1 MB (minimum allowed)
        TaskName orchestratorName = nameof(Autochunk_MultipleChunks_CompletesSuccessfully);
        TaskName activityName = "Echo";

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            // Set a small chunk size to force chunking
            b.UseGrpc(opt => opt.MaxCompleteOrchestrationWorkItemSizePerChunk = ChunkSize);
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
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal(ActivityCount, metadata.ReadOutputAs<int>());
    }

    /// <summary>
    /// Validates autochunking with many timers that together exceed the chunk size.
    /// </summary>
    [Fact]
    public async Task Autochunk_ManyTimers_CompletesSuccessfully()
    {
        // Use minimum allowed chunk size (1 MB) and use many timers to exceed it
        // Timers are small, so we need a large number to exceed 1 MB chunk size
        // Using 10000 timers which should be sufficient to test chunking without being too slow
        const int TimerCount = 10000;
        const int ChunkSize = GrpcDurableTaskWorkerOptions.MinCompleteOrchestrationWorkItemSizePerChunkBytes; // 1 MB (minimum allowed)
        TaskName orchestratorName = nameof(Autochunk_ManyTimers_CompletesSuccessfully);

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            // Set a small chunk size to force chunking
            b.UseGrpc(opt => opt.MaxCompleteOrchestrationWorkItemSizePerChunk = ChunkSize);
            b.AddTasks(tasks => tasks.AddOrchestratorFunc(orchestratorName, async ctx =>
            {
                // Start all timers in parallel so they're all in the same completion response
                List<Task> timerTasks = new List<Task>();
                for (int i = 0; i < TimerCount; i++)
                {
                    timerTasks.Add(ctx.CreateTimer(TimeSpan.FromMilliseconds(10), CancellationToken.None));
                }
                await Task.WhenAll(timerTasks);
                return TimerCount;
            }));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal(TimerCount, metadata.ReadOutputAs<int>());
    }

    /// <summary>
    /// Validates autochunking with mixed action types (activities, timers, sub-orchestrations).
    /// </summary>
    [Fact]
    public async Task Autochunk_MixedActions_CompletesSuccessfully()
    {
        // Use minimum allowed chunk size (1 MB) and ensure total payload exceeds it to trigger chunking
        const int ActivityCount = 300; // 300 activities × 2KB = 600KB, plus timers and sub-orchestrations to exceed 1 MB
        const int TimerCount = 1000; // Additional timers to help exceed chunk size
        const int SubOrchCount = 50; // Additional sub-orchestrations
        const int PayloadSizePerActivity = 2 * 1024; // 2KB per activity
        const int ChunkSize = GrpcDurableTaskWorkerOptions.MinCompleteOrchestrationWorkItemSizePerChunkBytes; // 1 MB (minimum allowed)
        TaskName orchestratorName = nameof(Autochunk_MixedActions_CompletesSuccessfully);
        TaskName activityName = "Echo";
        TaskName subOrchName = "SubOrch";

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            // Set a small chunk size to force chunking
            b.UseGrpc(opt => opt.MaxCompleteOrchestrationWorkItemSizePerChunk = ChunkSize);
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
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal(ActivityCount + TimerCount + SubOrchCount, metadata.ReadOutputAs<int>());
    }

    /// <summary>
    /// Validates that an InvalidOperationException is thrown when a single orchestrator action
    /// exceeds the MaxCompleteOrchestrationWorkItemSizePerChunk limit.
    /// </summary>
    [Fact]
    public async Task Autochunk_SingleActionExceedsChunkSize_ThrowsInvalidOperationException()
    {
        const int ChunkSize = GrpcDurableTaskWorkerOptions.MinCompleteOrchestrationWorkItemSizePerChunkBytes; // 1 MB
        // Create a payload that exceeds the chunk size (1 MB + some overhead)
        const int PayloadSize = ChunkSize + 100 * 1024; // 1.1 MB payload
        TaskName orchestratorName = nameof(Autochunk_SingleActionExceedsChunkSize_ThrowsInvalidOperationException);
        TaskName activityName = "Echo";

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.UseGrpc(opt => opt.MaxCompleteOrchestrationWorkItemSizePerChunk = ChunkSize);
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
        
        // Wait a bit for the orchestration to process and the exception to be thrown
        await Task.Delay(TimeSpan.FromSeconds(2), this.TimeoutToken);
        
        // The exception is caught and the work item is abandoned, so the orchestration won't complete.
        // Instead, verify the exception was thrown by checking the logs.
        IReadOnlyCollection<LogEntry> logs = this.GetLogs();
        
        // Find the log entry with the InvalidOperationException
        LogEntry? errorLog = logs.FirstOrDefault(log =>
            log.Exception is InvalidOperationException &&
            log.Exception.Message.Contains("exceeds the", StringComparison.OrdinalIgnoreCase) &&
            log.Exception.Message.Contains("MB limit", StringComparison.OrdinalIgnoreCase));
        
        Assert.NotNull(errorLog);
        Assert.NotNull(errorLog.Exception);
        Assert.IsType<InvalidOperationException>(errorLog.Exception);
        
        // Verify the error message contains the expected information
        Assert.Contains("exceeds the", errorLog.Exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MB limit", errorLog.Exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ScheduleTask", errorLog.Exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

