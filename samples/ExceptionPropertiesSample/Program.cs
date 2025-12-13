// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// This sample demonstrates how to use IExceptionPropertiesProvider to enrich
// TaskFailureDetails with custom exception properties for better diagnostics.

using ExceptionPropertiesSample;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

string? schedulerConnectionString = builder.Configuration.GetValue<string>("DURABLE_TASK_SCHEDULER_CONNECTION_STRING");
bool useScheduler = !string.IsNullOrWhiteSpace(schedulerConnectionString);

// Register the durable task client
if (useScheduler)
{
    builder.Services.AddDurableTaskClient(clientBuilder => clientBuilder.UseDurableTaskScheduler(schedulerConnectionString!));
}
else
{
    builder.Services.AddDurableTaskClient().UseGrpc();
}

// Register the durable task worker with custom exception properties provider
if (useScheduler)
{
    builder.Services.AddDurableTaskWorker(workerBuilder =>
    {
        workerBuilder.AddTasks(tasks =>
        {
            tasks.AddOrchestrator<ValidationOrchestration>();
            tasks.AddActivity<ValidateInputActivity>();
        });

        workerBuilder.UseDurableTaskScheduler(schedulerConnectionString!);
    });
}
else
{
    builder.Services.AddDurableTaskWorker()
        .AddTasks(tasks =>
        {
            tasks.AddOrchestrator<ValidationOrchestration>();
            tasks.AddActivity<ValidateInputActivity>();
        })
        .UseGrpc();
}

// Register the custom exception properties provider
// This will automatically extract custom properties from exceptions and include them in TaskFailureDetails
builder.Services.AddSingleton<IExceptionPropertiesProvider, CustomExceptionPropertiesProvider>();

IHost host = builder.Build();

// Start the worker
await host.StartAsync();

// Get the client to start orchestrations
DurableTaskClient client = host.Services.GetRequiredService<DurableTaskClient>();

Console.WriteLine("Exception Properties Sample");
Console.WriteLine("===========================");
Console.WriteLine();

Console.WriteLine(useScheduler
    ? "Configured to use Durable Task Scheduler (DTS)."
    : "Configured to use local gRPC. (Set DURABLE_TASK_SCHEDULER_CONNECTION_STRING to use DTS.)");
Console.WriteLine();

// Test case 1: Valid input (should succeed)
Console.WriteLine("Test 1: Valid input");
string instanceId1 = await client.ScheduleNewOrchestrationInstanceAsync(
    "ValidationOrchestration",
    input: "Hello World");

OrchestrationMetadata result1 = await client.WaitForInstanceCompletionAsync(
    instanceId1,
    getInputsAndOutputs: true);

if (result1.RuntimeStatus == OrchestrationRuntimeStatus.Completed)
{
    Console.WriteLine($"✓ Orchestration completed successfully");
    Console.WriteLine($"  Output: {result1.ReadOutputAs<string>()}");
}
Console.WriteLine();

// Test case 2: Empty input (should fail with custom properties)
Console.WriteLine("Test 2: Empty input (should fail)");
string instanceId2 = await client.ScheduleNewOrchestrationInstanceAsync(
    "ValidationOrchestration",
    input: string.Empty);

OrchestrationMetadata result2 = await client.WaitForInstanceCompletionAsync(
    instanceId2,
    getInputsAndOutputs: true);

if (result2.RuntimeStatus == OrchestrationRuntimeStatus.Failed && result2.FailureDetails != null)
{
    Console.WriteLine($"✗ Orchestration failed as expected");
    Console.WriteLine($"  Error Type: {result2.FailureDetails.ErrorType}");
    Console.WriteLine($"  Error Message: {result2.FailureDetails.ErrorMessage}");
    
    // Display custom properties that were extracted by IExceptionPropertiesProvider
    if (result2.FailureDetails.Properties != null && result2.FailureDetails.Properties.Count > 0)
    {
        Console.WriteLine($"  Custom Properties:");
        foreach (var property in result2.FailureDetails.Properties)
        {
            Console.WriteLine($"    - {property.Key}: {property.Value}");
        }
    }
}
Console.WriteLine();

// Test case 3: Short input (should fail with different custom properties)
Console.WriteLine("Test 3: Short input (should fail)");
string instanceId3 = await client.ScheduleNewOrchestrationInstanceAsync(
    "ValidationOrchestration",
    input: "Hi");

OrchestrationMetadata result3 = await client.WaitForInstanceCompletionAsync(
    instanceId3,
    getInputsAndOutputs: true);

if (result3.RuntimeStatus == OrchestrationRuntimeStatus.Failed && result3.FailureDetails != null)
{
    Console.WriteLine($"✗ Orchestration failed as expected");
    Console.WriteLine($"  Error Type: {result3.FailureDetails.ErrorType}");
    Console.WriteLine($"  Error Message: {result3.FailureDetails.ErrorMessage}");
    
    // Display custom properties
    if (result3.FailureDetails.Properties != null && result3.FailureDetails.Properties.Count > 0)
    {
        Console.WriteLine($"  Custom Properties:");
        foreach (var property in result3.FailureDetails.Properties)
        {
            Console.WriteLine($"    - {property.Key}: {property.Value}");
        }
    }
}
Console.WriteLine();

Console.WriteLine("Sample completed. Press any key to exit...");
Console.ReadKey();

await host.StopAsync();

