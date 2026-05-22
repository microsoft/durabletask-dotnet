// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// This sample demonstrates opt-in unversioned fallback for per-task versioning.
// A worker can register one explicit legacy implementation for a known version
// and an unversioned implementation as the catch-all for versions that do not
// have an explicit [DurableTask(Version = "...")] registration.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
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
    wb.UseVersioning(new DurableTaskWorkerOptions.VersioningOptions
    {
        // Activity fallback is the safer place to start: activities are stateless and do not replay
        // history. Enable orchestrator fallback (commented below) only when the unversioned
        // orchestrator is replay-compatible with every version it may receive.
        ActivityUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.CatchAll,
        OrchestratorUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.CatchAll,
    });
    wb.UseWorkItemFilters();
    wb.UseDurableTaskScheduler(connectionString);
});

builder.Services.AddDurableTaskClient(cb => cb.UseDurableTaskScheduler(connectionString));

IHost host = builder.Build();
await host.StartAsync();

await using DurableTaskClient client = host.Services.GetRequiredService<DurableTaskClient>();

Console.WriteLine("=== Unversioned fallback for versioned task dispatch ===");
Console.WriteLine();

SupportRequest request = new("Contoso", "BGP session down");

Console.WriteLine("Scheduling SupportWorkflow version 1.4.0 ...");
string legacyId = await client.ScheduleNewOrchestrationInstanceAsync(
    nameof(SupportWorkflow),
    request,
    new StartOrchestrationOptions
    {
        Version = new TaskVersion("1.4.0"),
    });
OrchestrationMetadata legacy = await client.WaitForInstanceCompletionAsync(legacyId, getInputsAndOutputs: true);
Console.WriteLine($"  Result: {legacy.ReadOutputAs<string>()}");
Console.WriteLine();

Console.WriteLine("Scheduling SupportWorkflow version 1.0 ...");
string fallbackId = await client.ScheduleNewOrchestrationInstanceAsync(
    nameof(SupportWorkflow),
    request,
    new StartOrchestrationOptions
    {
        Version = new TaskVersion("1.0"),
    });
OrchestrationMetadata fallback = await client.WaitForInstanceCompletionAsync(fallbackId, getInputsAndOutputs: true);
Console.WriteLine($"  Result: {fallback.ReadOutputAs<string>()}");
Console.WriteLine();

Console.WriteLine("Done! Version 1.4.0 used the explicit legacy class; version 1.0 used the unversioned fallback.");

await host.StopAsync();

/// <summary>
/// The current implementation. With OrchestratorUnversionedFallback enabled, this unversioned registration
/// handles every requested SupportWorkflow version that does not have an exact explicit registration.
/// </summary>
[DurableTask(nameof(SupportWorkflow))]
public sealed class SupportWorkflow : TaskOrchestrator<SupportRequest, string>
{
    /// <inheritdoc />
    public override Task<string> RunAsync(TaskOrchestrationContext context, SupportRequest input)
    {
        return Task.FromResult(
            $"Current SupportWorkflow handled version '{context.Version}' for {input.Customer}: {input.Issue}");
    }
}

/// <summary>
/// A pinned legacy implementation for version 1.4.0.
/// </summary>
[DurableTask(nameof(SupportWorkflow), Version = "1.4.0")]
public sealed class SupportWorkflowLegacyV140 : TaskOrchestrator<SupportRequest, string>
{
    /// <inheritdoc />
    public override Task<string> RunAsync(TaskOrchestrationContext context, SupportRequest input)
    {
        return Task.FromResult(
            $"Legacy SupportWorkflow 1.4.0 handled version '{context.Version}' for {input.Customer}: {input.Issue}");
    }
}

/// <summary>
/// Request input for the support workflow.
/// </summary>
public sealed record SupportRequest(string Customer, string Issue);
