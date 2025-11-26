// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// This sample demonstrates how to use the IDurableTaskClientFactory to create
// DurableTaskClient instances dynamically at runtime with different configurations.
// This is useful when you need to interact with multiple task hubs or need to
// configure clients on-demand based on runtime conditions.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// ============================================================================
// Example 1: Basic setup with IDurableTaskClientFactory
// ============================================================================

Console.WriteLine("=== Durable Task Client Factory Sample ===\n");

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Get the scheduler endpoint from environment variable
string schedulerEndpoint = Environment.GetEnvironmentVariable("DURABLE_TASK_SCHEDULER_ENDPOINT")
    ?? "http://localhost:8080";

// Configure the base Durable Task client with gRPC
builder.Services.AddDurableTaskClient(clientBuilder =>
{
    clientBuilder.UseGrpc(schedulerEndpoint);

    // You can also configure named options for specific task hubs
    clientBuilder.Services.Configure<DurableTaskClientOptions>("taskHub-A", options =>
    {
        // Task hub specific configuration
        options.EnableEntitySupport = true;
    });

    clientBuilder.Services.Configure<DurableTaskClientOptions>("taskHub-B", options =>
    {
        // Different task hub configuration
        options.EnableEntitySupport = false;
    });
});

IHost host = builder.Build();

// ============================================================================
// Example 2: Using IDurableTaskClientFactory to create clients dynamically
// ============================================================================

Console.WriteLine("1. Getting IDurableTaskClientFactory from DI container...\n");

IDurableTaskClientFactory factory = host.Services.GetRequiredService<IDurableTaskClientFactory>();

Console.WriteLine("2. Creating default client (uses default configuration)...");
await using (DurableTaskClient defaultClient = factory.CreateClient())
{
    Console.WriteLine($"   Created client: Name='{defaultClient.Name}'");
}

Console.WriteLine("\n3. Creating named clients for different task hubs...");
await using (DurableTaskClient clientA = factory.CreateClient("taskHub-A"))
{
    Console.WriteLine($"   Created client for taskHub-A: Name='{clientA.Name}'");
}

await using (DurableTaskClient clientB = factory.CreateClient("taskHub-B"))
{
    Console.WriteLine($"   Created client for taskHub-B: Name='{clientB.Name}'");
}

// ============================================================================
// Example 3: Using CreateClient with custom options override
// ============================================================================

Console.WriteLine("\n4. Creating client with custom options override...");
await using (DurableTaskClient customClient = factory.CreateClient<DurableTaskClientOptions>(
    "custom-hub",
    options =>
    {
        // Override specific options at runtime
        options.EnableEntitySupport = true;
    }))
{
    Console.WriteLine($"   Created custom client: Name='{customClient.Name}'");
}

Console.WriteLine("\n=== Sample Complete ===");
Console.WriteLine("\nNote: This sample demonstrates the factory pattern for creating clients.");
Console.WriteLine("To actually start orchestrations, you would need a running Durable Task Scheduler.");

// ============================================================================
// Example 4: Common use case - Dynamic task hub selection based on runtime data
// ============================================================================

Console.WriteLine("\n--- Example: Dynamic Task Hub Selection ---\n");

// Simulated scenario: routing to different task hubs based on tenant
string[] tenants = ["tenant-A", "tenant-B", "tenant-C"];

foreach (string tenant in tenants)
{
    string taskHubName = GetTaskHubForTenant(tenant);

    await using DurableTaskClient client = factory.CreateClient(taskHubName);

    Console.WriteLine($"Tenant '{tenant}' -> Task Hub '{taskHubName}' (Client: '{client.Name}')");

    // In a real scenario, you would use this client to interact with the task hub:
    // await client.ScheduleNewOrchestrationInstanceAsync("ProcessTenantData", tenant);
}

Console.WriteLine();

// Helper function to determine task hub based on tenant
static string GetTaskHubForTenant(string tenant)
{
    // In a real application, this could:
    // - Look up configuration from a database
    // - Use consistent hashing to route to specific hubs
    // - Apply business logic to determine the appropriate hub
    return tenant switch
    {
        "tenant-A" => "taskHub-A",
        "tenant-B" => "taskHub-B",
        _ => "taskHub-A" // Default hub
    };
}
