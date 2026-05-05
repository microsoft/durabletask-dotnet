# Durable Task Middleware Console App Sample

This sample demonstrates how to register orchestration and activity middleware in a standalone Durable Task worker.

## What This Sample Does

1. Registers `GreetingOrchestrationMiddleware` with `UseOrchestrationMiddleware<T>()`.
2. Registers `GreetingActivityMiddleware` with `UseActivityMiddleware<T>()`.
3. Starts an orchestration with a typed input and tags.
4. Reads durable context from middleware, including task name, instance ID, typed input, tags, input type, and result.
5. Uses `CreateReplaySafeLogger<T>()` from orchestration middleware so log messages are not duplicated during replay.

## Running the Sample

This sample can run against either:

1. **Durable Task Scheduler (DTS)**: set the `DURABLE_TASK_SCHEDULER_CONNECTION_STRING` environment variable.
2. **Local gRPC endpoint**: if the environment variable is not set, the sample uses the default local gRPC configuration.

### DTS

Set `DURABLE_TASK_SCHEDULER_CONNECTION_STRING` and run the sample.

```cmd
set DURABLE_TASK_SCHEDULER_CONNECTION_STRING=Endpoint=https://...;TaskHub=...;Authentication=...;
dotnet run --project samples/MiddlewareConsoleApp/MiddlewareConsoleApp.csproj
```

```bash
export DURABLE_TASK_SCHEDULER_CONNECTION_STRING="Endpoint=https://...;TaskHub=...;Authentication=...;"
dotnet run --project samples/MiddlewareConsoleApp/MiddlewareConsoleApp.csproj
```

## Expected Output

The sample:

1. Starts a greeting orchestration for a tenant-specific request.
2. Logs orchestration middleware information before and after the orchestrator runs.
3. Logs activity middleware information before and after each activity runs.
4. Prints the greeting summary returned by the orchestration.

## Code Structure

- `Program.cs`: Contains the host setup, middleware registration, orchestration, activity, and middleware implementations.

## Key Code Snippet

```csharp
builder.Services.AddDurableTaskWorker()
    .UseOrchestrationMiddleware<GreetingOrchestrationMiddleware>()
    .UseActivityMiddleware<GreetingActivityMiddleware>()
    .AddTasks(tasks =>
    {
        tasks.AddOrchestrator<GreetingOrchestration>();
        tasks.AddActivity<SayHelloActivity>();
    })
    .UseGrpc();
```

## Notes

- Orchestration middleware runs during orchestrator replay. Avoid I/O, network calls, ambient clocks, random values, and non-durable awaits.
- Use replay-safe logging from `context.OrchestrationContext.CreateReplaySafeLogger<T>()` in orchestration middleware.
- Activity middleware is regular async code and can use dependency injection and external I/O, subject to normal activity at-least-once execution semantics.
