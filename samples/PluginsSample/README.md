# Plugins Sample

This sample demonstrates the **Durable Task Plugin system**, which is inspired by
[Temporal's plugin/interceptor pattern](https://docs.temporal.io/develop/plugins).

## What This Sample Shows

The sample registers all 5 built-in plugins on a Durable Task worker:

1. **LoggingPlugin** — Emits structured log events for orchestration and activity lifecycle events (start, complete, fail).
2. **MetricsPlugin** — Tracks execution counts and durations for orchestrations and activities.
3. **AuthorizationPlugin** — Runs authorization checks before task execution (using a custom `IAuthorizationHandler`).
4. **ValidationPlugin** — Validates input data before task execution (using a custom `IInputValidator`).
5. **RateLimitingPlugin** — Applies token-bucket rate limiting to activity dispatches.

## Prerequisites

- .NET 8.0 or later
- A running Durable Task Scheduler sidecar (emulator or DTS)

## Running the Sample

Start the DTS emulator:

```bash
docker run --name durabletask-emulator -d -p 4001:4001 mcr.microsoft.com/dts/dts-emulator:latest
```

Run the sample:

```bash
dotnet run
```

## Plugin Architecture

The plugin system follows these key design principles:

- **Composable** — Multiple plugins can be registered and they execute in registration order.
- **Non-invasive** — Plugins wrap orchestrations and activities through interceptors without modifying the core logic.
- **Temporal-aligned** — The `SimplePlugin` builder pattern mirrors Temporal's `SimplePlugin` for cross-ecosystem familiarity.

### Creating Custom Plugins

```csharp
// Create a custom plugin using the SimplePlugin builder
SimplePlugin myPlugin = SimplePlugin.NewBuilder("MyOrg.MyPlugin")
    .AddOrchestrationInterceptor(new MyOrchestrationInterceptor())
    .AddActivityInterceptor(new MyActivityInterceptor())
    .Build();

// Register it on the worker builder
builder.Services.AddDurableTaskWorker()
    .UsePlugin(myPlugin)
    .UseGrpc();
```
