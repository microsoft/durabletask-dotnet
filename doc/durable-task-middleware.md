# Durable Task middleware

Durable Task middleware lets you run cross-cutting code around orchestration and activity invocation. Use it for concerns such as replay-safe tracing, input validation, correlation metadata, tenant checks, activity metrics, result inspection, and host integration that would otherwise be duplicated across tasks.

Middleware runs in registration order before user code and unwinds in reverse order after user code:

```text
registered middleware 1
  -> registered middleware 2
    -> orchestrator or activity
  <- registered middleware 2
<- registered middleware 1
```

This page is for .NET application authors. Language SDK implementers should also read the [cross-SDK Durable Task middleware guidance](cross-sdk-middleware.md).

## Registration

### Standalone Durable Task workers

Register middleware when configuring the worker.

```csharp
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Middleware;

builder.Services.AddDurableTaskWorker()
    .UseOrchestrationMiddleware<MyOrchestrationMiddleware>()
    .UseActivityMiddleware<MyActivityMiddleware>();
```

Type-based middleware implements `ITaskOrchestrationMiddleware` or `ITaskActivityMiddleware`.

```csharp
using Microsoft.DurableTask;
using Microsoft.DurableTask.Worker.Middleware;
using Microsoft.Extensions.Logging;

sealed class MyOrchestrationMiddleware : ITaskOrchestrationMiddleware
{
    public async Task InvokeAsync(
        TaskOrchestrationMiddlewareContext context,
        TaskOrchestrationMiddlewareDelegate next)
    {
        ILogger logger = context.OrchestrationContext.CreateReplaySafeLogger<MyOrchestrationMiddleware>();
        logger.LogInformation("Starting orchestration {Name} ({InstanceId}).", context.Name, context.InstanceId);

        await next(context);

        logger.LogInformation("Completed orchestration {Name} with result {Result}.", context.Name, context.Result);
    }
}

sealed class MyActivityMiddleware(ILogger<MyActivityMiddleware> logger) : ITaskActivityMiddleware
{
    public async Task InvokeAsync(TaskActivityMiddlewareContext context, TaskActivityMiddlewareDelegate next)
    {
        logger.LogInformation("Starting activity {Name} for {InstanceId}.", context.Name, context.InstanceId);

        await next(context);

        logger.LogInformation("Completed activity {Name} with result {Result}.", context.Name, context.Result);
    }
}
```

Delegate overloads are also available. Delegate instances may be reused across invocations, so avoid capturing unsynchronized mutable state.

```csharp
UseOrchestrationMiddleware(Func<TaskOrchestrationMiddlewareContext, TaskOrchestrationMiddlewareDelegate, Task> handler)
UseActivityMiddleware(Func<TaskActivityMiddlewareContext, TaskActivityMiddlewareDelegate, Task> handler)
```

```csharp
builder.Services.AddDurableTaskWorker()
    .UseOrchestrationMiddleware(async (context, next) =>
    {
        await next(context);
    })
    .UseActivityMiddleware(async (context, next) =>
    {
        await next(context);
    });
```

### Azure Functions .NET isolated

In .NET isolated Durable Functions apps, register Durable Task middleware with `ConfigureDurableWorker()` in `Program.cs`.

```csharp
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.DurableTask.Worker;

FunctionsApplicationBuilder builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureDurableWorker()
    .UseOrchestrationMiddleware<LoggingOrchestrationMiddleware>()
    .UseActivityMiddleware<LoggingActivityMiddleware>();
```

Azure Functions adds the current `FunctionContext` to the middleware feature collection. When your app references the .NET isolated Durable Functions extension package, use its `GetFunctionContext()` extension method from orchestration or activity middleware when the middleware is running inside the Functions extension.

```csharp
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Worker.Middleware;

FunctionContext? functionContext = context.GetFunctionContext();
string? functionName = functionContext?.FunctionDefinition.Name;
```

`GetFunctionContext()` returns `null` when the middleware context was not created by the Azure Functions extension. It works for .NET isolated Durable Functions function-syntax orchestrators and activities and for class-based forms.

## Middleware contexts

### Orchestration middleware context

`TaskOrchestrationMiddlewareContext` exposes durable orchestration invocation data:

| Property | Description |
|---|---|
| `Name` | Logical orchestration name. |
| `InstanceId` | Current orchestration instance ID. |
| `Version` | Orchestration version. |
| `Parent` | Parent orchestration instance, or `null`. |
| `Tags` | Read-only orchestration tags, or `null`. |
| `IsReplaying` | Whether the orchestrator is replaying prior history. |
| `InputType` | Declared orchestration input type. |
| `Input` | Deserialized orchestration input. |
| `RawInput` | Raw serialized orchestration input, if available. |
| `OrchestrationContext` | The `TaskOrchestrationContext` used by the orchestrator. |
| `Features` | Per-work-item host feature collection. |
| `CancellationToken` | Cancellation token exposed by the context. In v1, this is `CancellationToken.None`. |
| `Result` | Orchestration result after `next(context)` returns. |
| `GetInput<T>()` | Reads `Input` as `T`, or throws if the value is not assignable. |

Orchestration middleware intentionally does not expose a service provider and does not expose `SetResult`. In v1, orchestration middleware that completes successfully must call `next(context)` exactly once. It may observe `context.Result` after `next(context)` returns, but it cannot replace or short-circuit the orchestration result.

### Activity middleware context

`TaskActivityMiddlewareContext` exposes durable activity invocation data:

| Property or method | Description |
|---|---|
| `Name` | Logical activity name. |
| `InstanceId` | Orchestration instance ID that scheduled the activity. |
| `InputType` | Declared activity input type. |
| `Input` | Deserialized activity input. |
| `RawInput` | Raw serialized activity input, if available. |
| `ActivityContext` | The `TaskActivityContext` used by the activity. |
| `Features` | Per-work-item host feature collection. |
| `Services` | Activity invocation service provider. |
| `CancellationToken` | Cancellation token exposed by the context. In v1, this is `CancellationToken.None`. |
| `Result` | Activity result after `next(context)` returns or after `SetResult(...)`. |
| `GetInput<T>()` | Reads `Input` as `T`, or throws if the value is not assignable. |
| `SetResult(object?)` | Sets the activity result. Call it before `next(context)` to skip user activity execution, or after `next(context)` to replace the activity result. |

Activity middleware may call `next(context)` once, or it may short-circuit by calling `context.SetResult(...)` and not calling `next(context)`. Middleware that calls `next(context)` may also call `SetResult(...)` afterward to replace or normalize the activity result.

## Host features

`IMiddlewareFeatures` is a type-keyed feature collection for host-specific data.

```csharp
T? Get<T>() where T : class;
void Set<T>(T? feature) where T : class;
```

Features are scoped to a single orchestration or activity work item and are not serialized into durable history. Hosts can use features to expose objects such as Azure Functions `FunctionContext` without changing Durable Task wire protocols. Because feature values are host objects, orchestration middleware must use them only in deterministic, replay-safe ways.

## Orchestration determinism

Orchestration middleware runs on the orchestrator execution path and is replayed from history. It must follow the same replay determinism rules as orchestrator code.

Do not call these directly from orchestration middleware:

- `DateTime.Now`, `DateTime.UtcNow`, `Guid.NewGuid()`, `Random`, or other nondeterministic APIs.
- File system, network, database, queue, or other outbound I/O APIs.
- Non-durable async APIs or arbitrary `Task` awaits.
- APIs that depend on mutable process, environment, thread, or global state.

Use durable context APIs instead, such as `context.OrchestrationContext.CurrentUtcDateTime`, `context.OrchestrationContext.NewGuid()`, durable timers, activities, sub-orchestrations, and external events. If middleware needs dynamic policy checks, network calls, or audit writes, move that work to activities, host code, or activity middleware.

For logging, use a replay-safe logger or explicitly suppress duplicate replay logs.

```csharp
ILogger logger = context.OrchestrationContext.CreateReplaySafeLogger<MyOrchestrationMiddleware>();

if (!context.IsReplaying)
{
    logger.LogInformation("Observed input {Input} for {InstanceId}.", context.Input, context.InstanceId);
}
```

The runtime applies the orchestration illegal-await guard to the orchestration middleware plus orchestrator pipeline where the SDK can detect non-durable awaits. This guard is diagnostic protection, not a substitute for writing deterministic orchestration middleware.

## v1 limits

- Orchestration middleware must call `next(context)` exactly once when it completes successfully.
- Orchestration middleware can observe `context.Result` after `next(context)` returns, but cannot replace the result in v1.
- Orchestration middleware cannot short-circuit successful orchestration execution in v1. To reject input, throw a deterministic exception.
- Activity middleware can short-circuit by calling `context.SetResult(...)` and skipping `next(context)`, or can call `SetResult(...)` after `next(context)` to replace the activity result.
- `CancellationToken` on orchestration and activity middleware contexts is always `CancellationToken.None` in v1; it does not propagate host or durable execution cancellation.
- Entity middleware is not part of v1.

## Migrating from Azure Functions worker middleware workarounds

Some .NET isolated Durable Functions apps previously used `IFunctionsWorkerMiddleware` to reflect into `IFunctionBindingsFeature`, parse private protobuf payloads, or call durable binding APIs before execution to inspect durable inputs. Do not use those workarounds for durable invocation inspection.

Use Durable Task middleware instead when you need durable-aware access to task name, instance ID, input, tags, `FunctionContext`, or result. Durable Task middleware runs at the Durable Task invocation layer, so it sees the deserialized durable input and the result after execution without depending on Azure Functions worker internals.

`IFunctionsWorkerMiddleware` is still the right place for Functions-wide concerns such as HTTP headers, authentication pre-processing, non-durable bindings, and host-level policies that apply to all functions. Use Durable Task middleware for per-orchestration and per-activity behavior that needs durable invocation data.

## Filters versus middleware

`IOrchestrationFilter` and `UseWorkItemFilters` are work-acceptance and routing filters. They are configured at the worker level and run before execution to decide which work items a worker should process.

Durable Task middleware wraps invocation. It is the right place for per-invocation cross-cutting behavior that needs typed input, result, durable context, dependency injection for activities, or host features.

## Samples

- [Standalone middleware console app](../samples/MiddlewareConsoleApp/README.md)
- Azure Functions middleware sample in the Azure Functions extension repository: `azure-functions-durable-extension/samples/durable-middleware`
