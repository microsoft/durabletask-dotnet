# Plugins Sample

This sample demonstrates the **Durable Task Plugin system**, which is inspired by
[Temporal's plugin pattern](https://docs.temporal.io/develop/plugins).

## What Plugins Can Do

Temporal-style plugins serve **two purposes**:

### 1. Reusable Activities and Orchestrations

Plugins can ship pre-built activities and orchestrations that users get automatically
when they register the plugin. This is the "import and use" pattern:

```csharp
// A plugin author creates a package with reusable activities
var stringUtilsPlugin = SimplePlugin.NewBuilder("MyOrg.StringUtils")
    .AddTasks(registry =>
    {
        registry.AddActivityFunc<string, string>("StringUtils.ToUpper",
            (ctx, input) => input.ToUpperInvariant());
        registry.AddActivityFunc<string, string>("StringUtils.Reverse",
            (ctx, input) => new string(input.Reverse().ToArray()));
    })
    .Build();

// Users just register the plugin — activities are available immediately
builder.Services.AddDurableTaskWorker()
    .UsePlugin(stringUtilsPlugin)
    .UseGrpc();

// Then call the plugin's activities from any orchestration
string upper = await context.CallActivityAsync<string>("StringUtils.ToUpper", "hello");
```

### 2. Cross-Cutting Interceptors

Plugins can add lifecycle interceptors for concerns like logging, metrics, auth, etc.

## Built-in Cross-Cutting Plugins

| Plugin | Description |
|--------|-------------|
| **LoggingPlugin** | Structured `ILogger` events for orchestration/activity lifecycle |
| **MetricsPlugin** | Execution counts, durations, success/failure tracking |
| **AuthorizationPlugin** | `IAuthorizationHandler` checks before execution |
| **ValidationPlugin** | `IInputValidator` input validation before execution |
| **RateLimitingPlugin** | Token-bucket rate limiting for activity dispatches |

## Prerequisites

- .NET 8.0 or later

## Running the Sample

```bash
dotnet run
```

(Uses the in-process test host — no external sidecar needed.)

## Plugin Architecture

The plugin system follows these key design principles:

- **Dual-purpose** — Plugins can provide reusable tasks AND/OR cross-cutting interceptors.
- **Composable** — Multiple plugins can be registered and they execute in registration order.
- **Auto-registering** — Plugin tasks are automatically registered into the worker's task registry.
- **Temporal-aligned** — The `SimplePlugin` builder pattern mirrors Temporal's `SimplePlugin`.
