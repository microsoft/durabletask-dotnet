// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
    // Ensure entity APIs are enabled for the client
    b.Configure(o => { o.EnableEntitySupport = true; o.EnableLargePayloadSupport = true; });
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

        // Entity samples
        // 1) Large entity operation input (worker externalizes input; entity receives resolved payload)
        tasks.AddOrchestratorFunc<object?, int>(
            "LargeEntityOperationInput",
            (ctx, _) => ctx.Entities.CallEntityAsync<int>(
                new EntityInstanceId(nameof(EchoLengthEntity), "1"),
                operationName: "EchoLength",
                input: new string('E', 700 * 1024)));
        tasks.AddEntity<EchoLengthEntity>(nameof(EchoLengthEntity));

        // 2) Large entity operation output (worker externalizes output; orchestrator reads resolved payload)
        tasks.AddOrchestratorFunc<object?, int>(
            "LargeEntityOperationOutput",
            async (ctx, _) => (await ctx.Entities.CallEntityAsync<string>(
                new EntityInstanceId(nameof(LargeResultEntity), "1"),
                operationName: "Produce",
                input: 850 * 1024)).Length);
        tasks.AddEntity<LargeResultEntity>(nameof(LargeResultEntity));

        // 3) Large entity state (worker externalizes state; client resolves on query)
        tasks.AddOrchestratorFunc<object?, object?>(
            "LargeEntityState",
            async (ctx, _) =>
            {
                await ctx.Entities.CallEntityAsync(
                    new EntityInstanceId(nameof(StateEntity), "1"),
                    operationName: "Set",
                    input: new string('S', 900 * 1024));
                return null;
            });
        tasks.AddEntity<StateEntity>(nameof(StateEntity));
    });
    b.UseExternalizedPayloads(opts =>
    {
        opts.ExternalizeThresholdBytes = 1024; // mirror client
        opts.ConnectionString = builder.Configuration.GetValue<string>("DURABLETASK_STORAGE") ?? "UseDevelopmentStorage=true";
        opts.ContainerName = builder.Configuration.GetValue<string>("DURABLETASK_PAYLOAD_CONTAINER");
    });
    // Ensure entity APIs are enabled for the worker
    b.Configure(o => { o.EnableEntitySupport = true; o.EnableLargePayloadSupport = true; });
});

IHost host = builder.Build();
await host.StartAsync();

await using DurableTaskClient client = host.Services.GetRequiredService<DurableTaskClient>();

// Option A: Directly pass an oversized input to orchestration to trigger externalization
string largeInput = new string('B', 1024 * 1024); // 1MB
string instanceId = await client.ScheduleNewOrchestrationInstanceAsync("LargeInputEcho", largeInput);
Console.WriteLine($"Started orchestration with direct large input. Instance: {instanceId}");


using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
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



// Run entity samples
Console.WriteLine();
Console.WriteLine("Running LargeEntityOperationInput...");
string largeEntityInput = new string('E', 700 * 1024); // 700KB
string entityInputInstance = await client.ScheduleNewOrchestrationInstanceAsync("LargeEntityOperationInput");
OrchestrationMetadata entityInputResult = await client.WaitForInstanceCompletionAsync(entityInputInstance, getInputsAndOutputs: true, cts.Token);
int entityInputLength = entityInputResult.ReadOutputAs<int>();
Console.WriteLine($"Status: {entityInputResult.RuntimeStatus}, Output length: {entityInputLength}");
Console.WriteLine($"Deserialized input length equals original: {entityInputLength == largeEntityInput.Length}");

Console.WriteLine();
Console.WriteLine("Running LargeEntityOperationOutput...");
int largeEntityOutputLength = 850 * 1024; // 850KB
string entityOutputInstance = await client.ScheduleNewOrchestrationInstanceAsync("LargeEntityOperationOutput");
OrchestrationMetadata entityOutputResult = await client.WaitForInstanceCompletionAsync(entityOutputInstance, getInputsAndOutputs: true, cts.Token);
int entityOutputLength = entityOutputResult.ReadOutputAs<int>();
Console.WriteLine($"Status: {entityOutputResult.RuntimeStatus}, Output length: {entityOutputLength}");
Console.WriteLine($"Deserialized output length equals original: {entityOutputLength == largeEntityOutputLength}");

Console.WriteLine();
Console.WriteLine("Running LargeEntityState and querying state...");
string largeEntityState = new string('S', 900 * 1024); // 900KB
string entityStateInstance = await client.ScheduleNewOrchestrationInstanceAsync("LargeEntityState");
OrchestrationMetadata entityStateOrch = await client.WaitForInstanceCompletionAsync(entityStateInstance, getInputsAndOutputs: true, cts.Token);
Console.WriteLine($"Status: {entityStateOrch.RuntimeStatus}");
EntityMetadata<string>? state = await client.Entities.GetEntityAsync<string>(new EntityInstanceId(nameof(StateEntity), "1"), includeState: true);
int stateLength = state?.State?.Length ?? 0;
Console.WriteLine($"State length: {stateLength}");
Console.WriteLine($"Deserialized state equals original: {state?.State == largeEntityState}");





public class EchoLengthEntity : TaskEntity<int>
{
    public int EchoLength(string input)
    {
        return input.Length;
    }
}

public class LargeResultEntity : TaskEntity<object?>
{
    public string Produce(int length)
    {
        return new string('R', length);
    }
}

public class StateEntity : TaskEntity<string?>
{
    protected override string? InitializeState(TaskEntityOperation entityOperation)
    {
        // Avoid Activator.CreateInstance<string>() which throws; start as null (no state)
        return null;
    }

    public void Set(string value)
    {
        this.State = value;
    }
}



