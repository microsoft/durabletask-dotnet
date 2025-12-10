// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// This sample demonstrates the use of strongly-typed external events with DurableEventAttribute.

using EventsSample;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDurableTaskClient().UseGrpc();
builder.Services.AddDurableTaskWorker()
    .AddTasks(tasks =>
    {
        tasks.AddOrchestrator<ApprovalOrchestrator>();
        tasks.AddActivity<NotifyApprovalRequiredActivity>();
        tasks.AddOrchestrator<DataProcessingOrchestrator>();
        tasks.AddActivity<ProcessDataActivity>();
    })
    .UseGrpc();

IHost host = builder.Build();
await host.StartAsync();

await using DurableTaskClient client = host.Services.GetRequiredService<DurableTaskClient>();

Console.WriteLine("=== Strongly-Typed Events Sample ===");
Console.WriteLine();

// Example 1: Approval workflow
Console.WriteLine("Starting approval workflow...");
string approvalInstanceId = await client.ScheduleNewOrchestrationInstanceAsync("ApprovalOrchestrator", "Important Request");
Console.WriteLine($"Started orchestration with ID: {approvalInstanceId}");
Console.WriteLine();

// Wait a moment for the notification to be sent
await Task.Delay(1000);

// Simulate approval
Console.WriteLine("Simulating approval event...");
await client.RaiseEventAsync(approvalInstanceId, "ApprovalEvent", new ApprovalEvent(true, "John Doe"));

// Wait for completion
OrchestrationMetadata approvalResult = await client.WaitForInstanceCompletionAsync(
    approvalInstanceId,
    getInputsAndOutputs: true);
Console.WriteLine($"Approval workflow result: {approvalResult.ReadOutputAs<string>()}");
Console.WriteLine();

// Example 2: Data processing workflow
Console.WriteLine("Starting data processing workflow...");
string dataInstanceId = await client.ScheduleNewOrchestrationInstanceAsync("DataProcessingOrchestrator", "test-input");
Console.WriteLine($"Started orchestration with ID: {dataInstanceId}");
Console.WriteLine();

// Wait a moment
await Task.Delay(1000);

// Send data event
Console.WriteLine("Sending data event...");
await client.RaiseEventAsync(dataInstanceId, "DataReceived", new DataReceivedEvent(123, "Sample Data"));

// Wait for completion
OrchestrationMetadata dataResult = await client.WaitForInstanceCompletionAsync(
    dataInstanceId,
    getInputsAndOutputs: true);
Console.WriteLine($"Data processing result: {dataResult.ReadOutputAs<string>()}");
Console.WriteLine();

Console.WriteLine("Sample completed successfully!");
await host.StopAsync();
