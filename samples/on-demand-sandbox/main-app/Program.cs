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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

const string Input = "on-demand-sandbox-sample";

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
string endpoint = builder.Configuration["OnDemandSandboxSample:EndpointAddress"]!;
string taskHub = builder.Configuration["OnDemandSandboxSample:TaskHubName"]!;
int orchestrationCount = GetOrchestrationCount(builder.Configuration);
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

    workerBuilder.ExcludeOnDemandSandboxActivities();
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
await sandboxActivitiesClient.EnableSandboxActivitiesAsync(taskHub);

DurableTaskClient client = host.Services.GetRequiredService<DurableTaskClient>();
List<string> instanceIds = new(orchestrationCount);
for (int index = 1; index <= orchestrationCount; index++)
{
    string input = orchestrationCount == 1 ? Input : $"{Input}-{index:D3}";
    string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
        OnDemandSandboxTaskNames.HelloOrchestrator,
        input: input);
    instanceIds.Add(instanceId);
    Console.WriteLine($"Started orchestration {index}/{orchestrationCount}: {instanceId}");
}

List<Task<OrchestrationMetadata>> completionTasks = new(orchestrationCount);
foreach (string instanceId in instanceIds)
{
    completionTasks.Add(client.WaitForInstanceCompletionAsync(
        instanceId,
        getInputsAndOutputs: true));
}

OrchestrationMetadata[] results = await Task.WhenAll(completionTasks);
int completedCount = 0;
for (int index = 0; index < results.Length; index++)
{
    OrchestrationMetadata? result = results[index];
    if (result?.RuntimeStatus == OrchestrationRuntimeStatus.Completed)
    {
        completedCount++;
    }

    Console.WriteLine($"Orchestration {index + 1}/{orchestrationCount}: {instanceIds[index]}");
    Console.WriteLine($"Runtime status: {result?.RuntimeStatus}");
    Console.WriteLine($"Output: {result?.SerializedOutput ?? "<null>"}");
}

Console.WriteLine($"Completed orchestrations: {completedCount}/{orchestrationCount}");

await host.StopAsync();

static int GetOrchestrationCount(IConfiguration configuration)
{
    string? configuredValue = configuration["OnDemandSandboxSample:OrchestrationCount"];
    if (int.TryParse(configuredValue, out int configuredCount) && configuredCount > 0)
    {
        return configuredCount;
    }

    return 1;
}
