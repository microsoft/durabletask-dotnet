// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// This sample demonstrates the *one* scenario where versioning + entities genuinely compose into
// something you couldn't do with either feature alone: a single long-running orchestration instance
// transitions its logic from v1 to v2 mid-life via ContinueAsNew(NewVersion = "..."), and the
// entity state it has been writing to survives that transition.
//
// Why this is different from the other versioning samples:
//   - PerOrchestratorVersioningSample shows two parallel instances at different versions.
//   - This sample shows ONE instance whose logic version changes while preserving external state.
//
// Scenario:
//   - JobLog is an unversioned [DurableTask] entity that tracks the count of processed jobs.
//   - ProcessJobsWorkflow has two versions:
//       v1 (buggy): reads JobLog, processes one job per cycle (the bug — should be two), then
//           ContinueAsNew with NewVersion = "2" to apply a fix for the remaining work.
//       v2 (fixed): reads JobLog, processes the remaining jobs in one batch, completes.
//   - The same instance ID runs through both versions. The JobLog count incremented by v1 is
//     visible to v2 — entity state outlived the version transition.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

string connectionString = builder.Configuration.GetValue<string>("DURABLE_TASK_SCHEDULER_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "Set DURABLE_TASK_SCHEDULER_CONNECTION_STRING. " +
        "For the local emulator: Endpoint=http://localhost:8080;TaskHub=default;Authentication=None");

builder.Services.AddDurableTaskWorker(wb =>
{
    wb.AddTasks(tasks => tasks.AddAllGeneratedTasks());
    wb.UseDurableTaskScheduler(connectionString);
});

builder.Services.AddDurableTaskClient(cb => cb.UseDurableTaskScheduler(connectionString));

IHost host = builder.Build();
await host.StartAsync();

await using DurableTaskClient client = host.Services.GetRequiredService<DurableTaskClient>();

Console.WriteLine("=== Entity-backed orchestration migrating from v1 to v2 mid-life ===");
Console.WriteLine();

EntityInstanceId logId = new("JobLog", "production-job-log");

// Start the orchestration on v1. The orchestration will process one job (incrementing the entity)
// and then ContinueAsNew with NewVersion = "2", continuing on v2 logic with the same instance ID.
Console.WriteLine("Starting ProcessJobsWorkflow at version v1 ...");
string instanceId = await client.ScheduleNewProcessJobsWorkflowV1InstanceAsync(input: 3);
Console.WriteLine($"  Instance ID: {instanceId}");

OrchestrationMetadata final = await client.WaitForInstanceCompletionAsync(
    instanceId, getInputsAndOutputs: true);

Console.WriteLine($"  Final status:  {final.RuntimeStatus}");
Console.WriteLine($"  Final output:  {final.ReadOutputAs<string>()}");
Console.WriteLine();

// The same instance ID was used throughout — only the orchestration version changed.
// The JobLog count reflects work done by both v1 (one job) AND v2 (the rest).
EntityMetadata<int>? logState = await client.Entities.GetEntityAsync<int>(logId);
Console.WriteLine($"JobLog (queried directly): {logState?.State} jobs recorded total");
Console.WriteLine();

Console.WriteLine("Done! A single orchestration instance transitioned from v1 to v2 mid-flight.");
Console.WriteLine("The JobLog entity preserved the count contributed by v1 across the version change.");

await host.StopAsync();

// ─────────────────────────────────────────────────────────────────────────────
// Entity — intentionally unversioned. Owns state that must survive across orchestration version
// transitions (the whole point of the sample).
// ─────────────────────────────────────────────────────────────────────────────

[DurableTask(nameof(JobLog))]
public sealed class JobLog : TaskEntity<int>
{
    public int Record(int jobsProcessed)
    {
        this.State += jobsProcessed;
        return this.State;
    }

    public int Get() => this.State;
}

// ─────────────────────────────────────────────────────────────────────────────
// Orchestrator — v1 is the original (buggy) implementation; v2 is the fix that the same instance
// migrates to via ContinueAsNew(NewVersion = "2").
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// ProcessJobsWorkflow v1 — original implementation. Processes one job per cycle (the bug — should
/// be two), records it on the JobLog entity, and then ContinueAsNew to v2 to pick up a fix for the
/// remaining work. In a real eternal workflow this might be a polling loop that runs forever and
/// gets migrated when a bug is discovered.
/// </summary>
[DurableTask("ProcessJobsWorkflow", Version = "1")]
public sealed class ProcessJobsWorkflowV1 : TaskOrchestrator<int, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, int totalJobs)
    {
        EntityInstanceId logId = new(nameof(JobLog), "production-job-log");

        // v1 processes a single job per cycle (the bug — it should process two at a time).
        int totalRecorded = await context.Entities.CallEntityAsync<int>(logId, nameof(JobLog.Record), 1);
        int remaining = totalJobs - totalRecorded;

        // Migrate the same instance to v2 to apply the fix for the remaining work. The entity
        // state we just incremented is preserved across this boundary.
        context.ContinueAsNew(new ContinueAsNewOptions
        {
            NewInput = totalJobs,
            NewVersion = "2",
        });

        // Returned but never observed — ContinueAsNew restarts the instance.
        return $"v1 processed 1 job; {remaining} remaining; migrating to v2";
    }
}

/// <summary>
/// ProcessJobsWorkflow v2 — the fixed implementation. Runs after v1 calls ContinueAsNew with
/// NewVersion = "2". The same orchestration instance is now executing v2 logic, and the JobLog
/// entity still reflects v1's earlier contribution.
/// </summary>
[DurableTask("ProcessJobsWorkflow", Version = "2")]
public sealed class ProcessJobsWorkflowV2 : TaskOrchestrator<int, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, int totalJobs)
    {
        EntityInstanceId logId = new(nameof(JobLog), "production-job-log");

        // Read the current count — this is non-zero because v1 already recorded a job before
        // migrating. Entity state survived the version transition.
        int countBeforeFix = await context.Entities.CallEntityAsync<int>(logId, nameof(JobLog.Get));
        int remaining = totalJobs - countBeforeFix;

        // v2 processes the remaining jobs in one batch (the fix).
        int totalRecorded = await context.Entities.CallEntityAsync<int>(logId, nameof(JobLog.Record), remaining);

        return $"v2 saw {countBeforeFix} jobs already processed by v1; processed {remaining} more; total now {totalRecorded}";
    }
}
