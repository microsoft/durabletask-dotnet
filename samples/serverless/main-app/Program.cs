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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

string endpoint = GetRequiredEnvironmentVariable("DTS_ENDPOINT");
string taskHub = Environment.GetEnvironmentVariable("DTS_TASK_HUB") ?? "ServerlessPocHub";
string input = args.Length > 0
    ? args[0]
    : Environment.GetEnvironmentVariable("DTS_SAMPLE_HELLO_INPUT") ?? "serverless-sample";
TokenCredential credential = new DefaultAzureCredential();

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
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
    input: input);
OrchestrationMetadata? result = await client.WaitForInstanceCompletionAsync(
    instanceId,
    getInputsAndOutputs: true);

Console.WriteLine($"Started orchestration: {instanceId}");
Console.WriteLine($"Runtime status: {result?.RuntimeStatus}");
Console.WriteLine($"Output: {result?.SerializedOutput ?? "<null>"}");

await host.StopAsync();

static string GetRequiredEnvironmentVariable(string name)
    => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"An environment variable named '{name}' is required.");

