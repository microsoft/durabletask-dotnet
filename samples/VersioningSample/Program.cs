// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// This sample demonstrates worker-level versioning by running the same orchestration name
// against two separate worker versions.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

string schedulerConnectionString = builder.Configuration.GetValue<string>("DURABLE_TASK_SCHEDULER_CONNECTION_STRING")
    ?? throw new InvalidOperationException("DURABLE_TASK_SCHEDULER_CONNECTION_STRING is not set.");

await RunWorkerLevelVersioningDemoAsync(schedulerConnectionString);

async Task RunWorkerLevelVersioningDemoAsync(string schedulerConnectionString)
{
    await RunWorkerScopedVersionAsync(schedulerConnectionString, "1.0", "Version 1 implementation");
    await RunWorkerScopedVersionAsync(schedulerConnectionString, "2.0", "Version 2 implementation");

    Console.WriteLine("Worker-level versioning keeps one implementation active per worker run.");
}

async Task RunWorkerScopedVersionAsync(string schedulerConnectionString, string workerVersion, string outputPrefix)
{
    HostApplicationBuilder scopedBuilder = Host.CreateApplicationBuilder();

    scopedBuilder.Services.AddDurableTaskClient(clientBuilder =>
    {
        clientBuilder.UseDurableTaskScheduler(schedulerConnectionString);
        clientBuilder.UseDefaultVersion(workerVersion);
    });

    scopedBuilder.Services.AddDurableTaskWorker(workerBuilder =>
    {
        workerBuilder.AddTasks(tasks =>
        {
            tasks.AddOrchestratorFunc<string, string>("WorkerLevelGreeting", (context, name) =>
                context.CallActivityAsync<string>("FormatWorkerGreeting", name));
            tasks.AddActivityFunc<string, string>("FormatWorkerGreeting", (context, name) =>
                $"{outputPrefix} says hello to {name} on worker version {workerVersion}.");
        });

        workerBuilder.UseDurableTaskScheduler(schedulerConnectionString);
        workerBuilder.UseVersioning(new DurableTaskWorkerOptions.VersioningOptions
        {
            Version = workerVersion,
            DefaultVersion = workerVersion,
            MatchStrategy = DurableTaskWorkerOptions.VersionMatchStrategy.Strict,
            FailureStrategy = DurableTaskWorkerOptions.VersionFailureStrategy.Fail,
        });
    });

    IHost host = scopedBuilder.Build();
    await host.StartAsync();

    try
    {
        await using DurableTaskClient client = host.Services.GetRequiredService<DurableTaskClient>();

        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
            "WorkerLevelGreeting",
            "Durable Task",
            new StartOrchestrationOptions { Version = workerVersion });

        OrchestrationMetadata completedInstance = await client.WaitForInstanceCompletionAsync(
            instanceId,
            getInputsAndOutputs: true);

        if (completedInstance.RuntimeStatus != OrchestrationRuntimeStatus.Completed)
        {
            throw new InvalidOperationException($"Worker version {workerVersion} completed with unexpected status {completedInstance.RuntimeStatus}.");
        }

        string output = completedInstance.ReadOutputAs<string>()
            ?? throw new InvalidOperationException($"Worker version {workerVersion} did not produce output.");

        Console.WriteLine($"Worker version {workerVersion} completed with output: {output}");
    }
    finally
    {
        await host.StopAsync();
    }
}
