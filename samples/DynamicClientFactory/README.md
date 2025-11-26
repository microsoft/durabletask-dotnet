# Dynamic Client Factory Sample

This sample demonstrates how to use the `IDurableTaskClientFactory` interface to create `DurableTaskClient` instances dynamically at runtime with different configurations.

## Overview

The `IDurableTaskClientFactory` interface enables scenarios where you need to:

- Interact with multiple task hubs from a single application
- Create clients on-demand based on runtime conditions
- Route operations to different task hubs based on tenant, region, or other criteria
- Configure clients dynamically without pre-registering all configurations

## Key Concepts

### IDurableTaskClientFactory vs IDurableTaskClientProvider

| Feature | IDurableTaskClientProvider | IDurableTaskClientFactory |
|---------|---------------------------|---------------------------|
| Purpose | Get pre-configured clients | Create clients dynamically |
| Lifetime | Singleton clients from DI | New instance per call |
| Configuration | Fixed at startup | Can be customized at runtime |
| Use Case | Single task hub scenarios | Multi-tenant/multi-hub scenarios |

### Creating Clients

```csharp
// Get the factory from DI
IDurableTaskClientFactory factory = serviceProvider.GetRequiredService<IDurableTaskClientFactory>();

// Create a default client
DurableTaskClient defaultClient = factory.CreateClient();

// Create a named client (uses named options)
DurableTaskClient namedClient = factory.CreateClient("my-task-hub");

// Create a client with custom options override
DurableTaskClient customClient = factory.CreateClient<GrpcDurableTaskClientOptions>(
    "custom-hub",
    options => options.EnableEntitySupport = true);
```

## Prerequisites

- .NET 8.0 SDK or later
- A running Durable Task Scheduler (optional, for actual orchestration execution)

## Running the Sample

1. Set the scheduler endpoint (optional):
   ```bash
   export DURABLE_TASK_SCHEDULER_ENDPOINT="http://localhost:8080"
   ```

2. Run the sample:
   ```bash
   dotnet run
   ```

The sample will demonstrate creating clients with different configurations and show how to route operations to different task hubs based on runtime conditions.

## Code Structure

- `Program.cs` - Main sample demonstrating:
  - Basic factory setup with DI
  - Creating default and named clients
  - Custom options override
  - Dynamic task hub selection based on tenant

## Learn More

- [IDurableTaskClientFactory Design Document](../../doc/design/dynamic-client-factory.md)
- [GitHub Issue #298](https://github.com/microsoft/durabletask-dotnet/issues/298)
