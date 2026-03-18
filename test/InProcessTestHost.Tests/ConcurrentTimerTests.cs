// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Testing;
using Microsoft.DurableTask.Worker;
using Xunit;

namespace InProcessTestHost.Tests;

/// <summary>
/// Tests to verify that multiple orchestrations with identical timer FireAt timestamps
/// all complete correctly without any being dropped.
/// </summary>
public class ConcurrentTimerTests
{
    [Fact]
    // Test that multi orchestrations with the same timer that fire at the same time
    // can all complete correctly.
    public async Task MultipleOrchestrations_WithSameTimerFireAt_AllComplete()
    {
        const int orchestrationCount = 10;
        const string orchestratorName = "TimerOrchestrator";

        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(tasks =>
        {
            tasks.AddOrchestratorFunc<DateTime, string>(orchestratorName, async (ctx, fireAt) =>
            {
                await ctx.CreateTimer(fireAt, CancellationToken.None);
                return $"done:{ctx.InstanceId}";
            });
        });

        DateTime sharedFireAt = DateTime.UtcNow.AddSeconds(10);

        string[] instanceIds = new string[orchestrationCount];
        for (int i = 0; i < orchestrationCount; i++)
        {
            instanceIds[i] = await host.Client.ScheduleNewOrchestrationInstanceAsync(
                orchestratorName, sharedFireAt);
        }

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));

        Task<OrchestrationMetadata>[] waitTasks = instanceIds
            .Select(id => host.Client.WaitForInstanceCompletionAsync(
                id, getInputsAndOutputs: true, cts.Token))
            .ToArray();

        OrchestrationMetadata[] results = await Task.WhenAll(waitTasks);

        for (int i = 0; i < orchestrationCount; i++)
        {
            Assert.NotNull(results[i]);
            Assert.Equal(OrchestrationRuntimeStatus.Completed, results[i].RuntimeStatus);
            string output = results[i].ReadOutputAs<string>()!;
            Assert.Equal($"done:{instanceIds[i]}", output);
        }
    }

    [Fact]
    // Test that fan-out sub-orchestrations with the same timer fire at time
    // can all complete correctly.
    public async Task SubOrchestrations_WithIdenticalTimers_AllComplete()
    {
        const int subOrchestrationCount = 10;
        const string parentName = "ParentOrchestrator";
        const string childName = "ChildTimerOrchestrator";

        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(tasks =>
        {
            tasks.AddOrchestratorFunc<int>(parentName, async ctx =>
            {
                DateTime sharedFireAt = ctx.CurrentUtcDateTime.AddSeconds(2);

                // A parent orchestration will schedule 10 sub-orchestrations which has a timer
                // fires at the same time.
                Task<string>[] childTasks = Enumerable.Range(0, subOrchestrationCount)
                    .Select(i => ctx.CallSubOrchestratorAsync<string>(childName, sharedFireAt))
                    .ToArray();

                string[] results = await Task.WhenAll(childTasks);
                return results.Length;
            });

            tasks.AddOrchestratorFunc<DateTime, string>(childName, async (ctx, fireAt) =>
            {
                await ctx.CreateTimer(fireAt, CancellationToken.None);
                return $"child-done:{ctx.InstanceId}";
            });
        });

        string instanceId = await host.Client.ScheduleNewOrchestrationInstanceAsync(parentName);

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(60));
        OrchestrationMetadata metadata = await host.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, cts.Token);

        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal(subOrchestrationCount, metadata.ReadOutputAs<int>());
    }
}
