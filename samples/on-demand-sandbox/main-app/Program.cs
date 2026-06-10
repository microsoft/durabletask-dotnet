// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
using Azure.Identity;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Samples.OnDemandSandbox.MainApp;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

const string Input = "on-demand-sandbox-sample";

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
string endpoint = builder.Configuration["OnDemandSandboxSample:EndpointAddress"]!;
string taskHub = builder.Configuration["OnDemandSandboxSample:TaskHubName"]!;
TokenCredential credential = new DefaultAzureCredential();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
});

builder.Services.AddDurableTaskWorker(workerBuilder =>
{
    workerBuilder.AddTasks(tasks => tasks.AddAllGeneratedTasks());
    workerBuilder.UseDurableTaskScheduler(options =>
    {
        options.EndpointAddress = endpoint;
        options.TaskHubName = taskHub;
        options.Credential = credential;
    });
});

builder.Services.AddDurableTaskClient(clientBuilder =>
{
    clientBuilder.UseDurableTaskScheduler(options =>
    {
        options.EndpointAddress = endpoint;
        options.TaskHubName = taskHub;
        options.Credential = credential;
    });
});
builder.Services.AddDurableTaskSchedulerOnDemandSandboxActivitiesClient();

using IHost host = builder.Build();

await host.StartAsync();

OnDemandSandboxActivitiesClient sandboxActivitiesClient = host.Services.GetRequiredService<OnDemandSandboxActivitiesClient>();
await sandboxActivitiesClient.EnableOnDemandSandboxActivitiesAsync();

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
