// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// This sample demonstrates per-orchestrator versioning with [DurableTaskVersion].
// Multiple implementations of the same logical orchestration name coexist in one
// worker process. The source generator produces version-qualified helper methods
// that route each instance to the correct implementation automatically.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Read the DTS connection string from configuration.
string connectionString = builder.Configuration.GetValue<string>("DURABLE_TASK_SCHEDULER_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "Set DURABLE_TASK_SCHEDULER_CONNECTION_STRING. " +
        "For the local emulator: Endpoint=http://localhost:8080;TaskHub=default;Authentication=None");

// Configure the worker. AddAllGeneratedTasks() registers every [DurableTask]-annotated
// class in the project — including both versions of OrderWorkflow and MigratingWorkflow.
builder.Services.AddDurableTaskWorker(wb =>
{
    wb.AddTasks(tasks => tasks.AddAllGeneratedTasks());
    wb.UseDurableTaskScheduler(connectionString);
});

// Configure the client.
builder.Services.AddDurableTaskClient(cb => cb.UseDurableTaskScheduler(connectionString));

IHost host = builder.Build();
await host.StartAsync();

await using DurableTaskClient client = host.Services.GetRequiredService<DurableTaskClient>();

Console.WriteLine("=== Per-orchestrator versioning ([DurableTaskVersion]) ===");
Console.WriteLine();

// 1) Schedule an OrderWorkflow version 1 instance.
//    The generated helper ScheduleNewOrderWorkflow_1InstanceAsync automatically
//    stamps the instance with version "1".
Console.WriteLine("Scheduling OrderWorkflow v1 ...");
string v1Id = await client.ScheduleNewOrderWorkflow_1InstanceAsync(5);
OrchestrationMetadata v1 = await client.WaitForInstanceCompletionAsync(v1Id, getInputsAndOutputs: true);
Console.WriteLine($"  Result: {v1.ReadOutputAs<string>()}");
Console.WriteLine();

// 2) Schedule an OrderWorkflow version 2 instance — same logical name, different logic.
Console.WriteLine("Scheduling OrderWorkflow v2 ...");
string v2Id = await client.ScheduleNewOrderWorkflow_2InstanceAsync(5);
OrchestrationMetadata v2 = await client.WaitForInstanceCompletionAsync(v2Id, getInputsAndOutputs: true);
Console.WriteLine($"  Result: {v2.ReadOutputAs<string>()}");
Console.WriteLine();

Console.WriteLine("Done! Both versions ran in the same worker process.");
Console.WriteLine();

// 3) Demonstrate ContinueAsNew version migration: the v1 orchestration migrates
//    to v2 using ContinueAsNewOptions.NewVersion. This is the safest migration point
//    for eternal orchestrations because the history is fully reset.
Console.WriteLine("Scheduling MigratingWorkflow v1 → v2 (ContinueAsNew migration) ...");
string migrateId = await client.ScheduleNewMigratingWorkflow_1InstanceAsync(new MigrationInput(10));
OrchestrationMetadata migrate = await client.WaitForInstanceCompletionAsync(migrateId, getInputsAndOutputs: true);
Console.WriteLine($"  Result: {migrate.ReadOutputAs<string>()}");
Console.WriteLine();

Console.WriteLine("Sample completed successfully!");
await host.StopAsync();

// ─────────────────────────────────────────────────────────────────────────────
// Orchestrator classes — same logical name, different versions
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// OrderWorkflow v1 — computes the total with no discount.
/// </summary>
[DurableTask("OrderWorkflow")]
[DurableTaskVersion("1")]
public sealed class OrderWorkflowV1 : TaskOrchestrator<int, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, int itemCount)
    {
        int total = itemCount * 10; // $10 per item
        return Task.FromResult($"Order total: ${total} (v1 — no discount)");
    }
}

/// <summary>
/// OrderWorkflow v2 — applies a 20% discount to orders of 5+ items.
/// </summary>
[DurableTask("OrderWorkflow")]
[DurableTaskVersion("2")]
public sealed class OrderWorkflowV2 : TaskOrchestrator<int, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, int itemCount)
    {
        int total = itemCount * 10;
        if (itemCount >= 5)
        {
            total = (int)(total * 0.8); // 20% discount
        }

        return Task.FromResult($"Order total: ${total} (v2 — with discount)");
    }
}

/// <summary>
/// MigratingWorkflow v1 — migrates to v2 via ContinueAsNew.
/// The input is an (itemCount, alreadyMigrated) tuple to guard against infinite loops
/// if the backend does not propagate NewVersion.
/// </summary>
[DurableTask("MigratingWorkflow")]
[DurableTaskVersion("1")]
public sealed class MigratingWorkflowV1 : TaskOrchestrator<MigrationInput, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, MigrationInput input)
    {
        if (input.AlreadyMigrated)
        {
            // NewVersion was not propagated — complete here instead of looping.
            return Task.FromResult($"Order total: ${input.ItemCount * 10} (v1 — migration not supported by backend)");
        }

        // Migrate to v2. The history is fully reset so there is no replay conflict risk.
        context.ContinueAsNew(new ContinueAsNewOptions
        {
            NewInput = new MigrationInput(input.ItemCount, AlreadyMigrated: true),
            NewVersion = "2",
        });

        return Task.FromResult(string.Empty);
    }
}

/// <summary>
/// MigratingWorkflow v2 — the target of the v1 → v2 migration.
/// </summary>
[DurableTask("MigratingWorkflow")]
[DurableTaskVersion("2")]
public sealed class MigratingWorkflowV2 : TaskOrchestrator<MigrationInput, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, MigrationInput input)
    {
        int total = input.ItemCount * 10;
        if (input.ItemCount >= 5)
        {
            total = (int)(total * 0.8);
        }

        return Task.FromResult($"Migrated order total: ${total} (v2 — after migration from v1)");
    }
}

/// <summary>
/// Input for the MigratingWorkflow orchestrators.
/// </summary>
public sealed record MigrationInput(int ItemCount, bool AlreadyMigrated = false);
