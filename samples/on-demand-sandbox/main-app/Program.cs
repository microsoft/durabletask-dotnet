// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Samples.OnDemandSandbox.MainApp;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

const string Input = "on-demand-sandbox-sample";

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
string connectionString = builder.Configuration.GetValue<string>("DURABLE_TASK_SCHEDULER_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Missing required configuration 'DURABLE_TASK_SCHEDULER_CONNECTION_STRING'");
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
});

builder.Services.AddDurableTaskWorker(workerBuilder =>
{
    workerBuilder.AddTasks(tasks => tasks.AddAllGeneratedTasks());
    workerBuilder.UseWorkItemFilters();
    workerBuilder.UseDurableTaskScheduler(connectionString);
});

builder.Services.AddDurableTaskClient(clientBuilder =>
{
    clientBuilder.UseDurableTaskScheduler(connectionString);
});
builder.Services.AddDurableTaskSchedulerSandboxActivitiesClient();

using IHost host = builder.Build();

await host.StartAsync();

SandboxActivitiesClient sandboxActivitiesClient = host.Services.GetRequiredService<SandboxActivitiesClient>();
await sandboxActivitiesClient.EnableSandboxActivitiesAsync();

DurableTaskClient client = host.Services.GetRequiredService<DurableTaskClient>();
string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
    OnDemandSandboxTaskNames.HelloOrchestrator,
    input: Input);
Console.WriteLine($"Started orchestration: {instanceId}");

OrchestrationMetadata result = await client.WaitForInstanceCompletionAsync(
    instanceId,
    getInputsAndOutputs: true);
Console.WriteLine($"Runtime status: {result.RuntimeStatus}");
Console.WriteLine($"Output: {result.SerializedOutput ?? "<null>"}");

await host.StopAsync();
