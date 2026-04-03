// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// This sample demonstrates worker-level versioning. Each worker deployment is pinned
// to a single version string via UseDefaultVersion(). The client stamps new orchestration
// instances with that version. To upgrade, you deploy a new worker binary with the
// updated implementation.
//
// This sample registers a single orchestration ("GreetingWorkflow") and shows how
// the version is associated with the orchestration instance.

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

// The worker version represents a deployment version. In production, you'd change this
// when deploying a new version of your worker with updated orchestration logic.
string workerVersion = builder.Configuration.GetValue<string>("WORKER_VERSION") ?? "1.0";

// Configure the worker with an orchestration.
builder.Services.AddDurableTaskWorker(wb =>
{
    wb.AddTasks(tasks =>
    {
        tasks.AddOrchestratorFunc<string, string>(
            "GreetingWorkflow",
            (ctx, name) => Task.FromResult($"Hello, {name}! (worker version: {ctx.Version})"));
    });

    wb.UseDurableTaskScheduler(connectionString);
});

// Configure the client. UseDefaultVersion stamps every new orchestration instance
// with this version automatically — no need to set it per-request.
builder.Services.AddDurableTaskClient(cb =>
{
    cb.UseDurableTaskScheduler(connectionString);
    cb.UseDefaultVersion(workerVersion);
});

IHost host = builder.Build();
await host.StartAsync();

await using DurableTaskClient client = host.Services.GetRequiredService<DurableTaskClient>();

Console.WriteLine($"=== Worker-level versioning (version: {workerVersion}) ===");
Console.WriteLine();

// Schedule a greeting orchestration. The version is automatically stamped by the client.
string instanceId = await client.ScheduleNewOrchestrationInstanceAsync("GreetingWorkflow", "World");
Console.WriteLine($"Started orchestration: {instanceId}");

OrchestrationMetadata result = await client.WaitForInstanceCompletionAsync(
    instanceId, getInputsAndOutputs: true);
Console.WriteLine($"Status: {result.RuntimeStatus}");
Console.WriteLine($"Output: {result.ReadOutputAs<string>()}");
Console.WriteLine();

Console.WriteLine("Try running again with WORKER_VERSION=2.0 to simulate a deployment upgrade.");

await host.StopAsync();
