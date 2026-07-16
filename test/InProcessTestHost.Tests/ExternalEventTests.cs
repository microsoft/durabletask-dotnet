// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Testing;
using Microsoft.DurableTask.Worker;
using Xunit;

namespace InProcessTestHost.Tests;

/// <summary>
/// Tests for external event delivery via <see cref="TaskOrchestrationContext.WaitForExternalEvent{T}(string, CancellationToken)"/>,
/// including the canonical "wait for an event with a timeout" pattern built on <see cref="Task.WhenAny(Task[])"/> and a
/// durable timer.
/// </summary>
/// <remarks>
/// A durable timer keeps an orchestration in the "Running" state until it expires or is cancelled. As documented on
/// <see cref="TaskOrchestrationContext.CreateTimer(TimeSpan, System.Threading.CancellationToken)"/>, all durable timers
/// must either expire or be cancelled via their <see cref="System.Threading.CancellationToken"/> before the orchestrator
/// can complete. When a timer is used only as a timeout (for example, with <see cref="Task.WhenAny(Task[])"/>), cancel it
/// once the other task wins so the orchestration completes promptly instead of waiting for the timer to fire. See
/// https://learn.microsoft.com/azure/azure-functions/durable/durable-functions-timers.
/// </remarks>
public class ExternalEventTests
{
    [Fact]
    // A plain external event is delivered to a waiting orchestration and its payload is returned.
    public async Task WaitForExternalEvent_IsDelivered()
    {
        const string orchestratorName = "WaitForEventOrchestrator";

        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(tasks =>
        {
            tasks.AddOrchestratorFunc<string>(orchestratorName, async ctx =>
            {
                return await ctx.WaitForExternalEvent<string>("MyEvent");
            });
        });

        string instanceId = await host.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));

        // Wait for the orchestration to start before raising the event.
        await host.Client.WaitForInstanceStartAsync(instanceId, cts.Token);
        await host.Client.RaiseEventAsync(instanceId, "MyEvent", "hello");

        OrchestrationMetadata metadata = await host.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, cts.Token);

        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("hello", metadata.ReadOutputAs<string>());
    }

    [Fact]
    // Canonical wait-for-event-with-timeout: when the event wins the Task.WhenAny, cancelling the durable timer lets the
    // orchestration complete promptly, well before the 5-minute timer would fire.
    public async Task WaitForExternalEvent_WithTimeout_EventWins()
    {
        const string orchestratorName = "EventWithTimeoutOrchestrator";

        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(tasks =>
        {
            tasks.AddOrchestratorFunc<string>(orchestratorName, async ctx =>
            {
                using CancellationTokenSource timerCts = new();
                using CancellationTokenSource eventCts = new();
                Task<string> eventTask = ctx.WaitForExternalEvent<string>("MyEvent", eventCts.Token);
                Task timerTask = ctx.CreateTimer(TimeSpan.FromMinutes(5), timerCts.Token);
                Task winner = await Task.WhenAny(eventTask, timerTask);
                if (winner == eventTask)
                {
                    // Cancel the still-pending durable timer so the orchestration can complete now.
                    timerCts.Cancel();
                    return await eventTask;
                }

                // Cancel the losing external-event wait, mirroring the SDK's WaitForExternalEvent(name, timeout) helper.
                eventCts.Cancel();
                return "timeout";
            });
        });

        string instanceId = await host.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));

        // Wait for the orchestration to start (and begin waiting on the event) before raising it,
        // while the 5-minute timer is still pending.
        await host.Client.WaitForInstanceStartAsync(instanceId, cts.Token);
        await host.Client.RaiseEventAsync(instanceId, "MyEvent", "event");

        OrchestrationMetadata metadata = await host.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, cts.Token);

        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("event", metadata.ReadOutputAs<string>());
    }

    [Fact]
    // Same pattern with a short timer and no event raised: the timer wins and the orchestration returns "timeout".
    public async Task WaitForExternalEvent_WithTimeout_TimerWins()
    {
        const string orchestratorName = "TimeoutWinsOrchestrator";

        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(tasks =>
        {
            tasks.AddOrchestratorFunc<string>(orchestratorName, async ctx =>
            {
                using CancellationTokenSource timerCts = new();
                using CancellationTokenSource eventCts = new();
                Task<string> eventTask = ctx.WaitForExternalEvent<string>("MyEvent", eventCts.Token);
                Task timerTask = ctx.CreateTimer(TimeSpan.FromSeconds(2), timerCts.Token);
                Task winner = await Task.WhenAny(eventTask, timerTask);
                if (winner == eventTask)
                {
                    timerCts.Cancel();
                    return await eventTask;
                }

                // The timer won: cancel the losing external-event wait so no wait is left outstanding.
                eventCts.Cancel();
                return "timeout";
            });
        });

        string instanceId = await host.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
        OrchestrationMetadata metadata = await host.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, cts.Token);

        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("timeout", metadata.ReadOutputAs<string>());
    }
}
