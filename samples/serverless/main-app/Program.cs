// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
using Azure.Identity;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Samples.Serverless.MainApp;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

const string Input = "serverless-sample";

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
string endpoint = builder.Configuration["ServerlessSample:EndpointAddress"]!;
string taskHub = builder.Configuration["ServerlessSample:TaskHubName"]!;
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

    workerBuilder.EnableServerlessActivities();
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

using IHost host = builder.Build();

await host.StartAsync();

DurableTaskClient client = host.Services.GetRequiredService<DurableTaskClient>();
string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
    ServerlessTaskNames.HelloOrchestrator,
    input: Input);
OrchestrationMetadata? result = await client.WaitForInstanceCompletionAsync(
    instanceId,
    getInputsAndOutputs: true);

Console.WriteLine($"Started orchestration: {instanceId}");
Console.WriteLine($"Runtime status: {result?.RuntimeStatus}");
Console.WriteLine($"Output: {result?.SerializedOutput ?? "<null>"}");

await host.StopAsync();
