// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Testing;
using Microsoft.DurableTask.Worker;
using Xunit;
using Xunit.Abstractions;

namespace InProcessTestHost.Tests;

/// <summary>
/// Tests that orchestrations using ContinueAsNew with activity calls and timers
/// resume correctly after each iteration and eventually complete.
/// </summary>
public class ContinueAsNewTests
{
    readonly ITestOutputHelper output;

    public ContinueAsNewTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    /// <summary>
    /// Registers a polling-style orchestrator and a stateful activity.
    ///
    /// TestOrchestrator:
    ///   1. Calls TestActivity with current state
    ///   2. Updates state from activity result
    ///   3. If not closed: waits 5s timer, then ContinueAsNew with updated state
    ///   4. If closed: orchestration ends
    ///
    /// TestActivity:
    ///   - First call: sets status to InProgress
    ///   - Second call: sets status to Succeeded (triggers orchestration completion)
    /// </summary>
    static void RegisterTestFunctions(DurableTaskRegistry tasks)
    {
        tasks.AddOrchestratorFunc<AsyncOperation>("TestOrchestrator", async (context, input) =>
        {
            var result = await context.CallActivityAsync<AsyncOperation>("TestActivity", input);
            input.Update(result);

            if (!input.Closed)
            {
                await context.CreateTimer(TimeSpan.FromSeconds(5), CancellationToken.None);
                context.ContinueAsNew(input);
                return;
            }
        });

        tasks.AddActivityFunc<AsyncOperation, AsyncOperation>("TestActivity", (context, input) =>
        {
            if (input.Status == Status.InProgress)
            {
                input.Status = Status.Succeeded;
            }
            else
            {
                input.Status = Status.InProgress;
            }

            return input;
        });
    }

    /// <summary>
    /// Verifies that a single orchestration calling an activity, waiting on a timer,
    /// and then using ContinueAsNew completes after 2 iterations without hanging.
    /// Covers the basic ContinueAsNew lifecycle: activity call -> state update ->
    /// timer -> ContinueAsNew -> activity call -> completion.
    /// </summary>
    [Fact]
    public async Task Orchestration_ContinueAsNew_WithActivityAndTimer_CompletesSuccessfully()
    {
        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(RegisterTestFunctions);

        var input = new AsyncOperation { Status = Status.NotStarted, Closed = false, IterationCount = 0 };
        string instanceId = await host.Client.ScheduleNewOrchestrationInstanceAsync("TestOrchestrator", input);

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(60));
        OrchestrationMetadata metadata = await host.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, cts.Token);

        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
    }

    /// <summary>
    /// Runs 50 ContinueAsNew orchestrations in parallel, repeated across 5 rounds
    /// with a fresh host each round. Validates that activity completion messages
    /// are correctly delivered under contention when many orchestrations compete
    /// for the dispatcher, ready-to-run queue, and instance locks simultaneously.
    /// Total: 250 orchestration instances across 5 rounds.
    /// </summary>
    [Fact]
    public async Task ConcurrentOrchestrations_ContinueAsNew_AllComplete()
    {
        const int orchestrationCount = 50;
        const int rounds = 5;

        for (int round = 0; round < rounds; round++)
        {
            this.output.WriteLine($"=== Round {round + 1}/{rounds} ===");

            await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(RegisterTestFunctions);

            string[] instanceIds = new string[orchestrationCount];
            for (int i = 0; i < orchestrationCount; i++)
            {
                var input = new AsyncOperation { Status = Status.NotStarted, Closed = false, IterationCount = 0 };
                instanceIds[i] = await host.Client.ScheduleNewOrchestrationInstanceAsync("TestOrchestrator", input);
            }

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(60));

            Task<OrchestrationMetadata>[] waitTasks = instanceIds
                .Select(id => host.Client.WaitForInstanceCompletionAsync(
                    id, getInputsAndOutputs: true, cts.Token))
                .ToArray();

            OrchestrationMetadata[] results = await Task.WhenAll(waitTasks);

            for (int i = 0; i < orchestrationCount; i++)
            {
                Assert.NotNull(results[i]);
                Assert.Equal(OrchestrationRuntimeStatus.Completed, results[i].RuntimeStatus);
            }

            this.output.WriteLine($"Round {round + 1}: all {orchestrationCount} completed");
        }
    }

    /// <summary>
    /// Schedules 100 ContinueAsNew orchestrations on a single host as fast as possible.
    /// All 100 share the same dispatcher and ready-to-run queue for their entire lifecycle,
    /// maximizing interleaving of activity completion messages and ContinueAsNew
    /// re-schedules across instances.
    /// </summary>
    [Fact]
    public async Task RapidFire_SequentialScheduling_ContinueAsNew_AllComplete()
    {
        const int orchestrationCount = 100;

        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(RegisterTestFunctions);

        string[] instanceIds = new string[orchestrationCount];
        for (int i = 0; i < orchestrationCount; i++)
        {
            var input = new AsyncOperation { Status = Status.NotStarted, Closed = false, IterationCount = 0 };
            instanceIds[i] = await host.Client.ScheduleNewOrchestrationInstanceAsync("TestOrchestrator", input);
        }

        this.output.WriteLine($"Scheduled {orchestrationCount} orchestrations on a single host");

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(120));

        Task<OrchestrationMetadata>[] waitTasks = instanceIds
            .Select(id => host.Client.WaitForInstanceCompletionAsync(
                id, getInputsAndOutputs: true, cts.Token))
            .ToArray();

        OrchestrationMetadata[] results = await Task.WhenAll(waitTasks);

        for (int i = 0; i < orchestrationCount; i++)
        {
            Assert.NotNull(results[i]);
            Assert.Equal(OrchestrationRuntimeStatus.Completed, results[i].RuntimeStatus);
        }

        this.output.WriteLine($"All {orchestrationCount} completed");
    }

    #region Models

    public enum Status
    {
        NotStarted,
        InProgress,
        Succeeded,
    }

    public class AsyncOperation
    {
        public Status Status { get; set; }

        public bool Closed { get; set; }

        public int IterationCount { get; set; }

        public void Update(AsyncOperation result)
        {
            this.Status = result.Status;
            this.IterationCount++;

            if (this.Status == Status.Succeeded)
            {
                this.Closed = true;
            }
        }
    }

    #endregion
}
