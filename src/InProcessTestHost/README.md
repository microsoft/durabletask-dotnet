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
