// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

// Demonstrates Large Payload Externalization using Azure Blob Storage.
// This sample uses Azurite/emulator by default via UseDevelopmentStorage=true.

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Connection string for Durable Task Scheduler
string schedulerConnectionString = builder.Configuration.GetValue<string>("DURABLE_TASK_SCHEDULER_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Missing required configuration 'DURABLE_TASK_SCHEDULER_CONNECTION_STRING'");

// Configure Durable Task client with Durable Task Scheduler and externalized payloads
builder.Services.AddDurableTaskClient(b =>
{
    b.UseDurableTaskScheduler(schedulerConnectionString);
    b.UseExternalizedPayloads(opts =>
    {
        // Keep threshold small to force externalization for demo purposes
        opts.ExternalizeThresholdBytes = 1024; // 1KB
        opts.ConnectionString = builder.Configuration.GetValue<string>("DURABLETASK_STORAGE") ?? "UseDevelopmentStorage=true";
        opts.ContainerName = builder.Configuration.GetValue<string>("DURABLETASK_PAYLOAD_CONTAINER");
    });
});

// Configure Durable Task worker with tasks and externalized payloads
builder.Services.AddDurableTaskWorker(b =>
{
    b.UseDurableTaskScheduler(schedulerConnectionString);
    b.AddTasks(tasks =>
    {
        // Orchestrator: call activity first, return its output (should equal original input)
        tasks.AddOrchestratorFunc<string, string>("LargeInputEcho", async (ctx, input) =>
        {
            string echoed = await ctx.CallActivityAsync<string>("Echo", input);
            return echoed;
        });

        // Activity: validate it receives raw input (not token) and return it
        tasks.AddActivityFunc<string, string>("Echo", (ctx, value) =>
        {
            if (value is null)
            {
                return string.Empty;
            }

            // If we ever see a token in the activity, externalization is not being resolved correctly.
            if (value.StartsWith("blob:v1:", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Activity received a payload token instead of raw input.");
            }

            return value;
        });
    });
    b.UseExternalizedPayloads(opts =>
    {
        opts.ExternalizeThresholdBytes = 1024; // mirror client
        opts.ConnectionString = builder.Configuration.GetValue<string>("DURABLETASK_STORAGE") ?? "UseDevelopmentStorage=true";
        opts.ContainerName = builder.Configuration.GetValue<string>("DURABLETASK_PAYLOAD_CONTAINER");
    });
});

IHost host = builder.Build();
await host.StartAsync();

await using DurableTaskClient client = host.Services.GetRequiredService<DurableTaskClient>();

// Option A: Directly pass an oversized input to orchestration to trigger externalization
string largeInput = new string('B', 1024 * 1024); // 1MB
string instanceId = await client.ScheduleNewOrchestrationInstanceAsync("LargeInputEcho", largeInput);
Console.WriteLine($"Started orchestration with direct large input. Instance: {instanceId}");


using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
OrchestrationMetadata result = await client.WaitForInstanceCompletionAsync(
    instanceId,
    getInputsAndOutputs: true,
    cts.Token);

Console.WriteLine($"RuntimeStatus: {result.RuntimeStatus}");
string deserializedInput = result.ReadInputAs<string>() ?? string.Empty;
string deserializedOutput = result.ReadOutputAs<string>() ?? string.Empty;

Console.WriteLine($"SerializedInput: {result.SerializedInput}");
Console.WriteLine($"SerializedOutput: {result.SerializedOutput}");
Console.WriteLine($"Deserialized input equals original: {deserializedInput == largeInput}");
Console.WriteLine($"Deserialized output equals original: {deserializedOutput == largeInput}");
Console.WriteLine($"Deserialized input length: {deserializedInput.Length}");



