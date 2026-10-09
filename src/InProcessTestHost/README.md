# DurableTaskTestHost - Testing Durable Orchestrations In-Process

`DurableTaskTestHost` is a simple API for testing your durable task orchestrations and activities **in-process** without requiring any external backend.

Supports both **class-based** and **function-based** syntax.

## Quick Start

### 1. Configure options (optional)

```csharp
var options = new DurableTaskTestHostOptions
{
    Port = 31000,                  // Optional: specific port (random by default)
    LoggerFactory = myLoggerFactory // Optional: pass logger factory for logging
};
```

### 2. Register test orchestrations and activities

```csharp
await using var testHost = await DurableTaskTestHost.StartAsync(registry =>
{
    // Class-based
    registry.AddOrchestrator<MyOrchestrator>();
    registry.AddActivity<MyActivity>();
    
    // Function-based
    registry.AddOrchestratorFunc("MyFunc", (ctx, input) => Task.FromResult("done"));
    registry.AddActivityFunc("MyActivity", (ctx, input) => Task.FromResult("result"));
});
```

### 3. Test

```csharp
string instanceId = await testHost.Client.ScheduleNewOrchestrationInstanceAsync("MyOrchestrator");
var result = await testHost.Client.WaitForInstanceCompletionAsync(instanceId);
```

### 4. Inspect orchestration history

```csharp
var history = await testHost.Client.GetOrchestrationHistoryAsync(instanceId);
```

History reads return an ordered snapshot of the current execution's **committed events**,
including the terminal event after completion, regardless of history size. Events from an
in-flight orchestration episode are not included until that episode commits. A pending
instance with no committed events returns an empty list; an unknown or purged instance
throws `ArgumentException`.

Only the current execution is retained. `ContinueAsNew` replaces the previous generation's
history when the new generation commits. The underlying in-memory service also accepts an
execution ID: null or empty selects the current execution, and a different execution ID
returns no history (gRPC `NotFound`). Worker history streaming continues to use the dispatched
episode's replay snapshot rather than this management snapshot. These temporary worker snapshots
remain available until the episode's final response and are released before the dispatcher commits
that episode or starts its next generation. A failed dispatch also releases its snapshot; committed
history is retained independently until purge or generation replacement.

### 5. Purge completed instances

```csharp
PurgeResult purgeResult = await testHost.Client.PurgeAllInstancesAsync(
    new PurgeInstancesFilter(Statuses: new[] { OrchestrationRuntimeStatus.Completed }));
```

`CreatedFrom` and `CreatedTo` are optional: omit either for an unbounded end of the
creation-time range. You can provide `CreatedTo` without `CreatedFrom`. Explicit
bounds are inclusive and are evaluated in UTC.

## Work-Item Completion Ownership

The in-process sidecar assigns a fresh token to every activity and orchestration delivery
using the existing gRPC `WorkItem.completionToken` field. Completion is matched to that
delivery, not just its logical instance or activity task ID. Duplicate and stale completion
requests cannot settle a replacement delivery.

**Migration for direct gRPC callers:** copy the received `WorkItem.completionToken` into
`ActivityResponse.completionToken` or `OrchestratorResponse.completionToken` on every
whole completion request. Keep the original instance ID and, for activities, the task ID
from that delivery. Missing tokens or mismatched identities return `InvalidArgument`; unknown, wrong-kind, or already
settled tokens return `NotFound`. A rejected request does not consume a valid delivery.
The SDK worker already echoes these tokens. SDK and protobuf implementations are unchanged.

The test host accepts a single full orchestration completion response only. Deprecated partial
or chunked completion is not supported: `isPartial = true` or any present `chunkIndex`
(including zero on a final fragment) returns `InvalidArgument` without consuming the delivery
or retaining actions. Direct callers must send all actions in one response, with `isPartial`
false and `chunkIndex` omitted. The SDK's existing oversized-response chunking path is unchanged
and is not supported by this test host.

Replay history streaming is separate and remains supported. A full response releases the
delivery's temporary replay snapshot, while an already captured history reader can still finish
independently.

An accepted completion takes precedence over a later failure of that delivery's pending
stream write. A send failure without an accepted completion still propagates. Closing the
`GetWorkItems` stream does not implicitly settle already delivered work; completion can
arrive through an independent RPC. No timeout, heartbeat, or lease behavior is added.

Explicit abandonment is unchanged: both Abandon RPCs only acknowledge requests and do not
validate tokens, cancel execution, or requeue work. Honoring abandonment is left to the
dependent follow-up [#814](https://github.com/microsoft/durabletask-dotnet/pull/814).

## Dependency Injection

When your activities depend on services, there are two approaches:

| Approach | When to Use |
|----------|-------------|
| **Option 1: ConfigureServices** | Simple tests where you register a few services directly |
| **Option 2: AddInMemoryDurableTask** | When you have an existing host (e.g., `WebApplicationFactory`) with complex DI setup |

### Option 1: ConfigureServices

Use this when you want the test host to manage everything. Register services directly in the test host options.

```csharp
await using var host = await DurableTaskTestHost.StartAsync(
    tasks =>
    {
        tasks.AddOrchestrator<MyOrchestrator>();
        tasks.AddActivity<MyActivity>();
    },
    new DurableTaskTestHostOptions
    {
        ConfigureServices = services =>
        {
            // Register services required by your orchestrator or activity function
            services.AddSingleton<IMyService, MyService>();
            services.AddSingleton<IUserRepository, InMemoryUserRepository>();
            services.AddLogging();
        }
    });

var instanceId = await host.Client.ScheduleNewOrchestrationInstanceAsync(nameof(MyOrchestrator), "input");
var result = await host.Client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: true);
```

Access registered services via `host.Services`:

```csharp
var myService = host.Services.GetRequiredService<IMyService>();
```

### Option 2: AddInMemoryDurableTask

Use this when you already have a host with complex DI setup (database, auth, external APIs, etc.) and want to add durable task testing to it.

```csharp
public class MyIntegrationTests : IAsyncLifetime
{
    IHost host = null!;
    DurableTaskClient client = null!;

    public async Task InitializeAsync()
    {
        this.host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                // Your existing services (from Program.cs, Startup.cs, etc.)
                services.AddSingleton<IUserRepository, InMemoryUserRepository>();
                services.AddScoped<IOrderService, OrderService>();
                services.AddDbContext<MyDbContext>();

                // Add in-memory durable task support
                services.AddInMemoryDurableTask(tasks =>
                {
                    tasks.AddOrchestrator<MyOrchestrator>();
                    tasks.AddActivity<MyActivity>();
                });
            })
            .Build();

        await this.host.StartAsync();
        this.client = this.host.Services.GetRequiredService<DurableTaskClient>();
    }
}
```

Access the in-memory orchestration service:

```csharp
var orchestrationService = host.Services.GetInMemoryOrchestrationService();
```

## API Reference

### DurableTaskTestHostOptions

| Property | Type | Description |
|----------|------|-------------|
| `Port` | `int?` | Specific port for gRPC sidecar. Random 30000-40000 if not set. |
| `LoggerFactory` | `ILoggerFactory?` | Logger factory for capturing logs during tests. |
| `ConfigureServices` | `Action<IServiceCollection>?` | Callback to register services for DI. |

### DurableTaskTestHost

| Property | Type | Description |
|----------|------|-------------|
| `Client` | `DurableTaskClient` | Client for scheduling and managing orchestrations. |
| `Services` | `IServiceProvider` | Service provider with registered services. |

### Extension Methods

| Method | Description |
|--------|-------------|
| `services.AddInMemoryDurableTask(configureTasks)` | Adds in-memory durable task support to an existing `IServiceCollection`. |
| `services.GetInMemoryOrchestrationService()` | Gets the `InMemoryOrchestrationService` from the service provider. |

## More Samples

See [BasicOrchestrationTests.cs](../../test/InProcessTestHost.Tests/BasicOrchestrationTests.cs), [DependencyInjectionTests.cs](../../test/InProcessTestHost.Tests/DependencyInjectionTests.cs), and [WebApplicationFactoryIntegrationTests.cs](../../test/InProcessTestHost.Tests/WebApplicationFactoryIntegrationTests.cs) for complete samples.
