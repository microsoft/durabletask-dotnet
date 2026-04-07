// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Testing;
using Microsoft.DurableTask.Worker;
using Xunit;

namespace InProcessTestHost.Tests;

/// <summary>
/// Tests to verify that orchestrations using ContinueAsNew correctly resume
/// after activity completion, even when the activity completes very quickly.
/// Reproduces the race condition described in GitHub issue #689.
/// </summary>
public class ContinueAsNewTests
{
    [Fact]
    public async Task ContinueAsNew_ActivityCompletesQuickly_OrchestrationResumes()
    {
        const string orchestratorName = "ContinueAsNewOrchestrator";
        const string activityName = "QuickActivity";

        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(tasks =>
        {
            tasks.AddOrchestratorFunc<int, string>(orchestratorName, async (ctx, iteration) =>
            {
                string result = await ctx.CallActivityAsync<string>(activityName, iteration);

                if (iteration < 2)
                {
                    ctx.ContinueAsNew(iteration + 1);
                    return string.Empty;
                }

                return result;
            });

            tasks.AddActivityFunc<int, string>(activityName, (ctx, iteration) =>
            {
                return $"done-iteration-{iteration}";
            });
        });

        string instanceId = await host.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, 0);

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
        OrchestrationMetadata metadata = await host.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, cts.Token);

        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("done-iteration-2", metadata.ReadOutputAs<string>());
    }

    [Fact]
    public async Task ContinueAsNew_WithTimer_ActivityCompletesQuickly_OrchestrationResumes()
    {
        const string orchestratorName = "ContinueAsNewTimerOrchestrator";
        const string activityName = "QuickTimerActivity";

        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(tasks =>
        {
            tasks.AddOrchestratorFunc<int, string>(orchestratorName, async (ctx, iteration) =>
            {
                string result = await ctx.CallActivityAsync<string>(activityName, iteration);

                if (iteration < 2)
                {
                    await ctx.CreateTimer(TimeSpan.FromMilliseconds(1), CancellationToken.None);
                    ctx.ContinueAsNew(iteration + 1);
                    return string.Empty;
                }

                return result;
            });

            tasks.AddActivityFunc<int, string>(activityName, (ctx, iteration) =>
            {
                return $"timer-done-{iteration}";
            });
        });

        string instanceId = await host.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, 0);

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
        OrchestrationMetadata metadata = await host.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, cts.Token);

        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("timer-done-2", metadata.ReadOutputAs<string>());
    }

    [Fact]
    public async Task ContinueAsNew_MultipleConcurrentOrchestrations_AllComplete()
    {
        const int orchestrationCount = 5;
        const string orchestratorName = "ConcurrentContinueAsNewOrchestrator";
        const string activityName = "ConcurrentQuickActivity";

        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(tasks =>
        {
            tasks.AddOrchestratorFunc<int, string>(orchestratorName, async (ctx, iteration) =>
            {
                string result = await ctx.CallActivityAsync<string>(activityName, iteration);

                if (iteration < 3)
                {
                    ctx.ContinueAsNew(iteration + 1);
                    return string.Empty;
                }

                return result;
            });

            tasks.AddActivityFunc<int, string>(activityName, (ctx, iteration) =>
            {
                return $"concurrent-done-{iteration}";
            });
        });

        string[] instanceIds = new string[orchestrationCount];
        for (int i = 0; i < orchestrationCount; i++)
        {
            instanceIds[i] = await host.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, 0);
        }

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(60));

        Task<OrchestrationMetadata>[] waitTasks = instanceIds
            .Select(id => host.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, cts.Token))
            .ToArray();

        OrchestrationMetadata[] results = await Task.WhenAll(waitTasks);

        for (int i = 0; i < orchestrationCount; i++)
        {
            Assert.NotNull(results[i]);
            Assert.Equal(OrchestrationRuntimeStatus.Completed, results[i].RuntimeStatus);
            Assert.Equal("concurrent-done-3", results[i].ReadOutputAs<string>());
        }
    }
}
