// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;
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
        const int ActivityCount = 15;
        const int PayloadSizePerActivity = 3 * 1024; // 3KB per activity
        const int ChunkSize = 10 * 1024; // 10KB chunks (small to force chunking)
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
        const int TimerCount = 100;
        const int ChunkSize = 100; // 100B chunks
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
        const int ActivityCount = 8;
        const int TimerCount = 5;
        const int SubOrchCount = 3;
        const int PayloadSizePerActivity = 2 * 1024; // 2KB per activity
        const int ChunkSize = 8 * 1024; // 8KB chunks
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
}

