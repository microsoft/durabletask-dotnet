# Dynamic DurableTaskClient Factory

## Overview

This document describes the design of the `IDurableTaskClientFactory` feature, which enables dynamic creation of `DurableTaskClient` instances at runtime for different task hubs.

## Problem Statement

In the non-isolated Azure Functions worker model, users could trigger orchestrations in different task hubs using overloads like:

```csharp
Task RaiseEventAsync(string taskHubName, string instanceId, string eventName, object eventData, string connectionName = null);
```

In the isolated worker model and the new Durable Task SDK, this capability was missing. Users needed a way to:

1. Dynamically create clients for task hubs specified at runtime (e.g., from HTTP route parameters)
2. Build multi-tenant applications where the task hub is determined dynamically

### Use Case Example

```csharp
[Function(nameof(WebhookHttpTrigger))]
public async Task RunAsync(
    [HttpTrigger(AuthorizationLevel.Anonymous, "POST", 
     Route = "MyWebhook/{taskHubName}/{orchestrationId}/{requestId}")] HttpRequestMessage req,
    [DurableClient] DurableTaskClient client,
    string taskHubName,  // Dynamic task hub from route
    ILogger logger)
{
    // Need to send event to a different task hub based on the route parameter
    // The [DurableClient] binding only provides the default configured client
}
```

## Solution Summary

The solution introduces a new `IDurableTaskClientFactory` interface that allows creating `DurableTaskClient` instances on-demand at runtime. Instead of requiring all task hub configurations to be registered at startup, users can now create clients dynamically by name:

```csharp
// Inject the factory
IDurableTaskClientFactory factory = ...;

// Create a client for any task hub at runtime
await using var client = factory.CreateClient("my-dynamic-task-hub");
await client.RaiseEventAsync(instanceId, "MyEvent", eventData);
```

This is different from the existing `IDurableTaskClientProvider`, which only retrieves pre-registered clients. The factory creates new, disposable client instances that the caller manages.

## Design Goals

1. **Non-Breaking**: The solution must not introduce breaking changes to existing APIs
2. **Flexible**: Support creating clients with custom configurations at runtime
3. **Consistent**: Follow existing patterns in the codebase (providers, builders, options)
4. **Disposable**: Created clients must be properly disposable
5. **Thread-Safe**: Factory must be safe to use from multiple threads

## Solution Design

### Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        Application Code                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌─────────────────────┐    ┌─────────────────────────────────┐ │
│  │ IDurableTaskClient  │    │    IDurableTaskClientFactory    │ │
│  │      Provider       │    │                                 │ │
│  │                     │    │  CreateClient(name)             │ │
│  │  GetClient(name)    │    │  CreateClient<TOptions>(...)    │ │
│  │  (pre-registered)   │    │  (dynamic creation)             │ │
│  └─────────────────────┘    └─────────────────────────────────┘ │
│           │                              │                      │
│           ▼                              ▼                      │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │               DurableTaskClient Instances                   ││
│  │  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐           ││
│  │  │Client A │ │Client B │ │Client C │ │Client D │  ...      ││
│  │  │(cached) │ │(cached) │ │(dynamic)│ │(dynamic)│           ││
│  │  └─────────┘ └─────────┘ └─────────┘ └─────────┘           ││
│  └─────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────┘
```

### Key Components

#### 1. IDurableTaskClientFactory Interface

```csharp
public interface IDurableTaskClientFactory
{
    /// <summary>
    /// Creates a new client with the specified name using default configuration.
    /// </summary>
    DurableTaskClient CreateClient(string? name = null);

    /// <summary>
    /// Creates a new client with custom configuration.
    /// </summary>
    DurableTaskClient CreateClient<TOptions>(string? name, Action<TOptions> configureOptions)
        where TOptions : DurableTaskClientOptions, new();
}
```

#### 2. DefaultDurableTaskClientFactory Implementation

The default implementation:
- Receives configuration about the client type (e.g., `GrpcDurableTaskClient`) from the builder
- Uses `IOptionsMonitor<TOptions>` to retrieve named options
- Creates new client instances using `Activator.CreateInstance`
- Supports applying custom option overrides

#### 3. Client Factory Configuration

```csharp
internal sealed class ClientFactoryConfiguration
{
    public Type ClientType { get; set; }      // e.g., typeof(GrpcDurableTaskClient)
    public Type OptionsType { get; set; }     // e.g., typeof(GrpcDurableTaskClientOptions)
}
```

### Registration Flow

```
services.AddDurableTaskClient(builder => 
{
    builder.UseGrpc();  // Registers ClientFactoryConfiguration
});

// Results in:
// 1. IDurableTaskClientProvider registered (for pre-registered clients)
// 2. IDurableTaskClientFactory registered (for dynamic creation)
// 3. ClientFactoryConfiguration registered (captures client type info)
```

### Comparison: Provider vs Factory

| Aspect | IDurableTaskClientProvider | IDurableTaskClientFactory |
|--------|---------------------------|--------------------------|
| Purpose | Retrieve pre-registered clients | Create new client instances |
| Lifetime | Singleton clients | Per-call disposable clients |
| Configuration | Set at startup | Can be modified per-call |
| Use Case | Standard dependency injection | Dynamic task hub access |
| Disposal | Managed by DI container | Caller's responsibility |

## Usage Patterns

### Pattern 1: Simple Dynamic Client Creation

```csharp
public class MyService
{
    private readonly IDurableTaskClientFactory _factory;

    public MyService(IDurableTaskClientFactory factory)
    {
        _factory = factory;
    }

    public async Task SendEventAsync(string taskHub, string instanceId, object data)
    {
        await using var client = _factory.CreateClient(taskHub);
        await client.RaiseEventAsync(instanceId, "MyEvent", data);
    }
}
```

### Pattern 2: Custom Options Configuration

```csharp
public async Task SendEventWithCustomOptionsAsync(string endpoint, string taskHub)
{
    await using var client = _factory.CreateClient<GrpcDurableTaskClientOptions>(
        taskHub,
        options =>
        {
            options.Address = endpoint;
            options.EnableEntitySupport = true;
        });
    
    await client.RaiseEventAsync("instance-1", "MyEvent", new { });
}
```

### Pattern 3: Client Caching for Repeated Access

```csharp
public class CachedClientService : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, DurableTaskClient> _cache = new();
    private readonly IDurableTaskClientFactory _factory;

    public DurableTaskClient GetOrCreateClient(string taskHub)
    {
        return _cache.GetOrAdd(taskHub, hub => _factory.CreateClient(hub));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _cache.Values)
        {
            await client.DisposeAsync();
        }
        _cache.Clear();
    }
}
```

## Thread Safety Considerations

1. **Factory**: The factory itself is stateless and thread-safe
2. **Options Monitor**: Uses the thread-safe `IOptionsMonitor<T>` pattern
3. **Created Clients**: Each call creates a new instance; thread safety depends on client implementation
4. **Caching**: If caching clients, use `ConcurrentDictionary` or similar

## Error Handling

| Error | Cause | Resolution |
|-------|-------|------------|
| `InvalidOperationException` | Factory not configured | Ensure `UseGrpc()` or similar is called |
| `ArgumentNullException` | Null configure action | Provide valid action |
| Connection errors | Invalid endpoint/hub | Validate configuration before use |

## Performance Considerations

1. **Client Creation**: Creating clients has overhead (gRPC channel setup)
2. **Recommendation**: Cache clients when making multiple calls to the same task hub
3. **Connection Pooling**: gRPC uses HTTP/2 connection pooling internally
4. **Dispose**: Always dispose dynamically created clients to release resources

## Future Considerations

1. **Built-in Caching**: Consider adding an optional caching layer
2. **Connection Pooling**: Explore sharing gRPC channels across clients
3. **Health Checks**: Add factory-level health check support
4. **Metrics**: Add telemetry for client creation/disposal

## Related Issues

- GitHub Issue: [#298 - RaiseEventAsync with a given taskhubname is missing](https://github.com/microsoft/durabletask-dotnet/issues/298)
