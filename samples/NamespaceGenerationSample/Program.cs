// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// This sample demonstrates how the source generator places extension methods into the same
// namespace as the orchestrator/activity classes, keeping IDE suggestions clean and scoped.
// Tasks in different namespaces get their own GeneratedDurableTaskExtensions class.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// The generated AddAllGeneratedTasks() method is always in Microsoft.DurableTask namespace.
// Extension methods like ScheduleNewApprovalOrchestratorInstanceAsync() are in the
// NamespaceGenerationSample.Approvals namespace, and CallRegistrationActivityAsync() is in
// NamespaceGenerationSample.Registrations namespace.
using NamespaceGenerationSample.Approvals;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Read the DTS connection string from configuration
string schedulerConnectionString = builder.Configuration.GetValue<string>("DURABLE_TASK_SCHEDULER_CONNECTION_STRING")
    ?? throw new InvalidOperationException("DURABLE_TASK_SCHEDULER_CONNECTION_STRING is not set.");

builder.Services.AddDurableTaskClient(clientBuilder => clientBuilder.UseDurableTaskScheduler(schedulerConnectionString));

builder.Services.AddDurableTaskWorker(workerBuilder =>
{
    // Use the generated AddAllGeneratedTasks() to register all orchestrators and activities
    workerBuilder.AddTasks(tasks => tasks.AddAllGeneratedTasks());
    workerBuilder.UseDurableTaskScheduler(schedulerConnectionString);
});

IHost host = builder.Build();
await host.StartAsync();

await using DurableTaskClient client = host.Services.GetRequiredService<DurableTaskClient>();

// Use the generated typed extension method (in the Approvals namespace)
string instanceId = await client.ScheduleNewApprovalOrchestratorInstanceAsync("request-123");
Console.WriteLine($"Started approval orchestration: {instanceId}");

// Wait for completion
OrchestrationMetadata? result = await client.WaitForInstanceCompletionAsync(
    instanceId, getInputsAndOutputs: true);
Console.WriteLine($"Orchestration completed with status: {result?.RuntimeStatus}");
Console.WriteLine($"Output: {result?.ReadOutputAs<string>()}");

await host.StopAsync();
