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
    ///   3. If not closed: waits 1s timer, then ContinueAsNew with updated state
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
                await context.CreateTimer(TimeSpan.FromSeconds(1), CancellationToken.None);
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
    [Fact(Timeout = 30_000)]
    public async Task Orchestration_ContinueAsNew_WithActivityAndTimer_CompletesSuccessfully()
    {
        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(RegisterTestFunctions);

        var input = new AsyncOperation { Status = Status.NotStarted, Closed = false, IterationCount = 0 };
        string instanceId = await host.Client.ScheduleNewOrchestrationInstanceAsync("TestOrchestrator", input);

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(20));
        OrchestrationMetadata metadata = await host.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, cts.Token);

        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
    }

    /// <summary>
    /// Runs 10 ContinueAsNew orchestrations in parallel, repeated across 3 rounds
    /// with a fresh host each round. Validates that activity completion messages
    /// are correctly delivered under contention when many orchestrations compete
    /// for the dispatcher, ready-to-run queue, and instance locks simultaneously.
    /// Total: 30 orchestration instances across 3 rounds.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task ConcurrentOrchestrations_ContinueAsNew_AllComplete()
    {
        const int orchestrationCount = 10;
        const int rounds = 3;

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
            }

            this.output.WriteLine($"Round {round + 1}: all {orchestrationCount} completed");
        }
    }

    /// <summary>
    /// Schedules 20 ContinueAsNew orchestrations on a single host as fast as possible.
    /// All 20 share the same dispatcher and ready-to-run queue for their entire lifecycle,
    /// maximizing interleaving of activity completion messages and ContinueAsNew
    /// re-schedules across instances.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task RapidFire_SequentialScheduling_ContinueAsNew_AllComplete()
    {
        const int orchestrationCount = 20;

        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(RegisterTestFunctions);

        string[] instanceIds = new string[orchestrationCount];
        for (int i = 0; i < orchestrationCount; i++)
        {
            var input = new AsyncOperation { Status = Status.NotStarted, Closed = false, IterationCount = 0 };
            instanceIds[i] = await host.Client.ScheduleNewOrchestrationInstanceAsync("TestOrchestrator", input);
        }

        this.output.WriteLine($"Scheduled {orchestrationCount} orchestrations on a single host");

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
        }

        this.output.WriteLine($"All {orchestrationCount} completed");
    }

    /// <summary>
    /// Schedules 30 ContinueAsNew orchestrations in 3 waves of 10, with 100ms delays
    /// between waves. The staggered scheduling creates overlapping lifecycle phases —
    /// earlier orchestrations may be mid-ContinueAsNew (waiting on timer or activity)
    /// when later waves arrive, producing varied timing patterns that exercise the
    /// interplay between lock release, message delivery, and queue re-scheduling.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task Staggered_ContinueAsNew_AllComplete()
    {
        const int wavesCount = 3;
        const int perWave = 10;

        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(RegisterTestFunctions);

        List<string> allInstanceIds = new();

        for (int wave = 0; wave < wavesCount; wave++)
        {
            for (int i = 0; i < perWave; i++)
            {
                var input = new AsyncOperation { Status = Status.NotStarted, Closed = false, IterationCount = 0 };
                string id = await host.Client.ScheduleNewOrchestrationInstanceAsync("TestOrchestrator", input);
                allInstanceIds.Add(id);
            }

            this.output.WriteLine($"Wave {wave + 1}/{wavesCount}: scheduled {perWave} orchestrations");
            await Task.Delay(100);
        }

        this.output.WriteLine($"Total scheduled: {allInstanceIds.Count}");

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));

        Task<OrchestrationMetadata>[] waitTasks = allInstanceIds
            .Select(id => host.Client.WaitForInstanceCompletionAsync(
                id, getInputsAndOutputs: true, cts.Token))
            .ToArray();

        OrchestrationMetadata[] results = await Task.WhenAll(waitTasks);

        for (int i = 0; i < results.Length; i++)
        {
            Assert.NotNull(results[i]);
            Assert.Equal(OrchestrationRuntimeStatus.Completed, results[i].RuntimeStatus);
        }

        this.output.WriteLine($"All {allInstanceIds.Count} completed");
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

/// <summary>
/// Tests targeting the ContinueAsNew race condition fix where stale messages from
/// prior ContinueAsNew iterations (activities, timers) could corrupt the new execution's
/// message queue or cause instances to get permanently stuck.
/// </summary>
public class ContinueAsNewRaceConditionTests
{
    readonly ITestOutputHelper output;

    public ContinueAsNewRaceConditionTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    /// <summary>
    /// Reproduces the original stuck-instance scenario: 4+ orchestrations running
    /// concurrently, each calling an activity, waiting on a timer, then ContinueAsNew,
    /// repeated for multiple iterations. Without the fix, some instances would stop
    /// making progress after 2-3 ContinueAsNew iterations because stale activity/timer
    /// messages from prior iterations would corrupt the message queue.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task ConcurrentContinueAsNew_MultipleIterations_NoneGetStuck()
    {
        const int instanceCount = 6;
        const int targetIterations = 5;

        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(tasks =>
        {
            tasks.AddOrchestratorFunc<int, int>("IteratingOrchestrator", async (context, iteration) =>
            {
                // Call activity
                await context.CallActivityAsync<string>("NoOpActivity", iteration);

                // Wait on timer (short delay to exercise timer message path)
                await context.CreateTimer(TimeSpan.FromMilliseconds(500), CancellationToken.None);

                if (iteration < targetIterations)
                {
                    context.ContinueAsNew(iteration + 1);
                    return -1; // unreachable
                }

                return iteration;
            });

            tasks.AddActivityFunc<int, string>("NoOpActivity", (context, iteration) =>
            {
                return $"done-{iteration}";
            });
        });

        // Schedule all instances concurrently
        string[] instanceIds = new string[instanceCount];
        for (int i = 0; i < instanceCount; i++)
        {
            instanceIds[i] = await host.Client.ScheduleNewOrchestrationInstanceAsync(
                "IteratingOrchestrator", 1);
        }

        this.output.WriteLine($"Scheduled {instanceCount} orchestrations, each targeting {targetIterations} iterations");

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(90));

        Task<OrchestrationMetadata>[] waitTasks = instanceIds
            .Select(id => host.Client.WaitForInstanceCompletionAsync(
                id, getInputsAndOutputs: true, cts.Token))
            .ToArray();

        OrchestrationMetadata[] results = await Task.WhenAll(waitTasks);

        for (int i = 0; i < instanceCount; i++)
        {
            Assert.NotNull(results[i]);
            Assert.Equal(OrchestrationRuntimeStatus.Completed, results[i].RuntimeStatus);
            int output = results[i].ReadOutputAs<int>();
            Assert.Equal(targetIterations, output);
            this.output.WriteLine($"Instance[{i}] ({instanceIds[i]}): completed at iteration {output}");
        }
    }

    /// <summary>
    /// Tests that an orchestration performing many ContinueAsNew iterations with
    /// both activities and timers on each iteration completes correctly. This exercises
    /// the fix that clears accumulated message lists between iterations — without it,
    /// stale timer/activity messages would accumulate and cause duplicate scheduling.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task SingleInstance_ManyContinueAsNewIterations_CompletesCorrectly()
    {
        const int targetIterations = 8;

        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(tasks =>
        {
            tasks.AddOrchestratorFunc<int, int>("MultiIterationOrchestrator", async (context, iteration) =>
            {
                // Each iteration: activity + timer + ContinueAsNew
                await context.CallActivityAsync<string>("EchoActivity", $"iter-{iteration}");
                await context.CreateTimer(TimeSpan.FromMilliseconds(100), CancellationToken.None);

                if (iteration < targetIterations)
                {
                    context.ContinueAsNew(iteration + 1);
                    return -1;
                }

                return iteration;
            });

            tasks.AddActivityFunc<string, string>("EchoActivity", (context, input) => $"echo:{input}");
        });

        string instanceId = await host.Client.ScheduleNewOrchestrationInstanceAsync(
            "MultiIterationOrchestrator", 1);

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
        OrchestrationMetadata metadata = await host.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, cts.Token);

        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal(targetIterations, metadata.ReadOutputAs<int>());
    }

    /// <summary>
    /// Verifies that ContinueAsNew works correctly when orchestrations call multiple
    /// activities per iteration. This amplifies the stale-message problem because each
    /// iteration generates more activity messages that must be properly discarded.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task ContinueAsNew_MultipleActivitiesPerIteration_AllComplete()
    {
        const int instanceCount = 4;
        const int activitiesPerIteration = 3;
        const int targetIterations = 4;

        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(tasks =>
        {
            tasks.AddOrchestratorFunc<int, int>("MultiActivityOrchestrator", async (context, iteration) =>
            {
                // Call multiple activities per iteration
                List<Task<string>> activityTasks = new();
                for (int i = 0; i < activitiesPerIteration; i++)
                {
                    activityTasks.Add(context.CallActivityAsync<string>(
                        "IndexedActivity", $"iter{iteration}-act{i}"));
                }

                await Task.WhenAll(activityTasks);
                await context.CreateTimer(TimeSpan.FromMilliseconds(200), CancellationToken.None);

                if (iteration < targetIterations)
                {
                    context.ContinueAsNew(iteration + 1);
                    return -1;
                }

                return iteration;
            });

            tasks.AddActivityFunc<string, string>("IndexedActivity", (context, input) => $"result:{input}");
        });

        string[] instanceIds = new string[instanceCount];
        for (int i = 0; i < instanceCount; i++)
        {
            instanceIds[i] = await host.Client.ScheduleNewOrchestrationInstanceAsync(
                "MultiActivityOrchestrator", 1);
        }

        this.output.WriteLine($"Scheduled {instanceCount} orchestrations with {activitiesPerIteration} activities per iteration");

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(45));
        Task<OrchestrationMetadata>[] waitTasks = instanceIds
            .Select(id => host.Client.WaitForInstanceCompletionAsync(
                id, getInputsAndOutputs: true, cts.Token))
            .ToArray();

        OrchestrationMetadata[] results = await Task.WhenAll(waitTasks);

        for (int i = 0; i < instanceCount; i++)
        {
            Assert.NotNull(results[i]);
            Assert.Equal(OrchestrationRuntimeStatus.Completed, results[i].RuntimeStatus);
            Assert.Equal(targetIterations, results[i].ReadOutputAs<int>());
        }
    }

    /// <summary>
    /// Runs the reproduction scenario from the original bug report repeatedly to ensure
    /// reliability: 4+ concurrent instances, each doing activity + 5s timer + ContinueAsNew,
    /// across multiple independent rounds with fresh hosts.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task ReproScenario_RepeatedRounds_AllComplete()
    {
        const int instanceCount = 4;
        const int rounds = 3;
        const int targetIterations = 3;

        for (int round = 0; round < rounds; round++)
        {
            this.output.WriteLine($"=== Round {round + 1}/{rounds} ===");

            await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(tasks =>
            {
                tasks.AddOrchestratorFunc<int, int>("ReproOrchestrator", async (context, iteration) =>
                {
                    await context.CallActivityAsync<string>("ReproActivity", iteration);
                    await context.CreateTimer(TimeSpan.FromSeconds(5), CancellationToken.None);

                    if (iteration < targetIterations)
                    {
                        context.ContinueAsNew(iteration + 1);
                        return -1;
                    }

                    return iteration;
                });

                tasks.AddActivityFunc<int, string>("ReproActivity", (context, input) => $"ok-{input}");
            });

            string[] instanceIds = new string[instanceCount];
            for (int i = 0; i < instanceCount; i++)
            {
                instanceIds[i] = await host.Client.ScheduleNewOrchestrationInstanceAsync(
                    "ReproOrchestrator", 1);
            }

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(45)); 
            Task<OrchestrationMetadata>[] waitTasks = instanceIds
                .Select(id => host.Client.WaitForInstanceCompletionAsync(
                    id, getInputsAndOutputs: true, cts.Token))
                .ToArray();

            OrchestrationMetadata[] results = await Task.WhenAll(waitTasks);

            for (int i = 0; i < instanceCount; i++)
            {
                Assert.NotNull(results[i]);
                Assert.Equal(OrchestrationRuntimeStatus.Completed, results[i].RuntimeStatus);
                Assert.Equal(targetIterations, results[i].ReadOutputAs<int>());
            }

            this.output.WriteLine($"Round {round + 1}: all {instanceCount} completed");
        }
    }
}
