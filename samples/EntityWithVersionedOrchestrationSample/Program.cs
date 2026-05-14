// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// This sample demonstrates that per-orchestrator versioning composes cleanly with durable entities.
// Entities are intentionally unversioned in this SDK — their state belongs to a single logical
// identity that persists across orchestration revisions. A versioned [DurableTask] orchestrator can
// freely call or signal an unversioned [DurableTask] entity, and the entity's state is shared across
// every orchestration version that touches it.
//
// In this sample:
// - `WalletEntity` is a single, unversioned entity that holds a balance.
// - `CheckoutWorkflow` has two versions (v1 and v2). v1 simply deducts the purchase price. v2 applies
//   a 10% loyalty discount before deducting.
// - Both versions write to the SAME wallet ("wallet-42"). The v2 orchestration sees the residual
//   state the v1 orchestration left behind, proving the entity is shared across versions.

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

// Read the DTS connection string from configuration.
string connectionString = builder.Configuration.GetValue<string>("DURABLE_TASK_SCHEDULER_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "Set DURABLE_TASK_SCHEDULER_CONNECTION_STRING. " +
        "For the local emulator: Endpoint=http://localhost:8080;TaskHub=default;Authentication=None");

// AddAllGeneratedTasks() registers every [DurableTask]-annotated class in the project — including the
// two versioned CheckoutWorkflow classes and the unversioned WalletEntity. UseDurableTaskScheduler
// enables entity support on both worker and client.
builder.Services.AddDurableTaskWorker(wb =>
{
    wb.AddTasks(tasks => tasks.AddAllGeneratedTasks());
    wb.UseDurableTaskScheduler(connectionString);
});

builder.Services.AddDurableTaskClient(cb => cb.UseDurableTaskScheduler(connectionString));

IHost host = builder.Build();
await host.StartAsync();

await using DurableTaskClient client = host.Services.GetRequiredService<DurableTaskClient>();

Console.WriteLine("=== Entities + per-orchestrator versioning ===");
Console.WriteLine();

// The same wallet identity is shared across both orchestration versions.
EntityInstanceId walletId = new("WalletEntity", "wallet-42");

// Seed the wallet with $100 so the v1 deduction has something to spend from.
await client.Entities.SignalEntityAsync(walletId, "Deposit", input: 100);
Console.WriteLine("Seeded wallet 'wallet-42' with $100.");
Console.WriteLine();

// 1) Run CheckoutWorkflow v1 — simple deduction.
Console.WriteLine("Scheduling CheckoutWorkflow v1 for $30 (no discount) ...");
string v1Id = await client.ScheduleNewCheckoutWorkflowV1InstanceAsync(30);
OrchestrationMetadata v1 = await client.WaitForInstanceCompletionAsync(v1Id, getInputsAndOutputs: true);
Console.WriteLine($"  Result: {v1.ReadOutputAs<string>()}");
Console.WriteLine();

// 2) Run CheckoutWorkflow v2 — applies a 10% discount, then deducts. Notice the running balance
//    reflects v1's earlier deduction: the entity is shared across versions.
Console.WriteLine("Scheduling CheckoutWorkflow v2 for $30 (10% loyalty discount applied) ...");
string v2Id = await client.ScheduleNewCheckoutWorkflowV2InstanceAsync(30);
OrchestrationMetadata v2 = await client.WaitForInstanceCompletionAsync(v2Id, getInputsAndOutputs: true);
Console.WriteLine($"  Result: {v2.ReadOutputAs<string>()}");
Console.WriteLine();

// 3) Query the entity directly to confirm the final state both orchestrations contributed to.
EntityMetadata<int>? walletState = await client.Entities.GetEntityAsync<int>(walletId);
Console.WriteLine($"Final wallet balance (queried directly): ${walletState?.State}");
Console.WriteLine();

Console.WriteLine("Done! The unversioned WalletEntity persisted state across both orchestration versions.");

await host.StopAsync();

// ─────────────────────────────────────────────────────────────────────────────
// Entity — intentionally unversioned. Per the proposal, entities are out of scope for [DurableTask]
// Version routing. A single entity identity persists across every orchestrator version that touches it.
// ─────────────────────────────────────────────────────────────────────────────

[DurableTask(nameof(WalletEntity))]
public sealed class WalletEntity : TaskEntity<int>
{
    public int Deposit(int amount)
    {
        this.State += amount;
        return this.State;
    }

    public int Withdraw(int amount)
    {
        this.State -= amount;
        return this.State;
    }

    public int Get() => this.State;
}

// ─────────────────────────────────────────────────────────────────────────────
// Orchestrators — two versions that share the same WalletEntity instance.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// CheckoutWorkflow v1 — deducts the purchase price directly from the wallet.
/// </summary>
[DurableTask("CheckoutWorkflow", Version = "1")]
public sealed class CheckoutWorkflowV1 : TaskOrchestrator<int, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, int price)
    {
        EntityInstanceId walletId = new(nameof(WalletEntity), "wallet-42");
        int newBalance = await context.Entities.CallEntityAsync<int>(walletId, nameof(WalletEntity.Withdraw), price);
        return $"v1 charged ${price}; new balance ${newBalance}";
    }
}

/// <summary>
/// CheckoutWorkflow v2 — applies a 10% loyalty discount, then deducts. Note that it uses the
/// <em>same</em> WalletEntity identity as v1; entities are unversioned and shared.
/// </summary>
[DurableTask("CheckoutWorkflow", Version = "2")]
public sealed class CheckoutWorkflowV2 : TaskOrchestrator<int, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, int price)
    {
        EntityInstanceId walletId = new(nameof(WalletEntity), "wallet-42");
        int discountedPrice = (int)Math.Round(price * 0.9);
        int newBalance = await context.Entities.CallEntityAsync<int>(walletId, nameof(WalletEntity.Withdraw), discountedPrice);
        return $"v2 charged ${discountedPrice} (was ${price}, 10% discount); new balance ${newBalance}";
    }
}
