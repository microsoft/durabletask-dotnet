# Replay-Safe Logger Factory Sample

This sample demonstrates how to wrap `TaskOrchestrationContext` and use the new `ReplaySafeLoggerFactory` property to preserve replay-safe logging.

## Overview

When you build helper libraries or decorators around `TaskOrchestrationContext`, C# protected access rules prevent you from delegating the protected `LoggerFactory` property from an inner context. This sample shows the recommended pattern:

```csharp
protected override ILoggerFactory LoggerFactory => innerContext.ReplaySafeLoggerFactory;
```

That approach keeps wrapper-level logging replay-safe while still allowing the wrapper to add orchestration-specific helper methods.

## What This Sample Does

1. Defines a `LoggingTaskOrchestrationContext` wrapper around `TaskOrchestrationContext`
2. Delegates the wrapper's `LoggerFactory` to `innerContext.ReplaySafeLoggerFactory`
3. Adds a `CallActivityWithLoggingAsync` helper that logs before and after an activity call
4. Runs an orchestration that uses the wrapper and completes with a simple greeting

## Running the Sample

This sample can run against either:

1. **Durable Task Scheduler (DTS)**: set the `DURABLE_TASK_SCHEDULER_CONNECTION_STRING` environment variable.
2. **Local gRPC endpoint**: if the environment variable is not set, the sample uses the default local gRPC configuration.

### DTS

Set `DURABLE_TASK_SCHEDULER_CONNECTION_STRING` and run the sample.

```cmd
set DURABLE_TASK_SCHEDULER_CONNECTION_STRING=Endpoint=https://...;TaskHub=...;Authentication=...;
dotnet run --project samples/ReplaySafeLoggerFactorySample/ReplaySafeLoggerFactorySample.csproj
```

```bash
export DURABLE_TASK_SCHEDULER_CONNECTION_STRING="Endpoint=https://...;TaskHub=...;Authentication=...;"
dotnet run --project samples/ReplaySafeLoggerFactorySample/ReplaySafeLoggerFactorySample.csproj
```

## Expected Output

The sample:

1. Starts a simple orchestration
2. Wraps the orchestration context
3. Calls an activity through a wrapper helper that uses replay-safe logging
4. Prints the orchestration result

## Code Structure

- `Program.cs`: Contains the host setup, orchestration, activity, and wrapper context

## Key Code Snippet

```csharp
internal sealed class LoggingTaskOrchestrationContext : TaskOrchestrationContext
{
    protected override ILoggerFactory LoggerFactory => this.innerContext.ReplaySafeLoggerFactory;

    public async Task<TResult> CallActivityWithLoggingAsync<TResult>(TaskName name, object? input = null)
    {
        ILogger logger = this.CreateReplaySafeLogger<LoggingTaskOrchestrationContext>();
        logger.LogInformation("Calling activity {ActivityName}.", name.Name);
        TResult result = await this.CallActivityAsync<TResult>(name, input);
        logger.LogInformation("Activity {ActivityName} completed.", name.Name);
        return result;
    }
}
```

## Notes

- The key design point is that the raw `LoggerFactory` remains protected on `TaskOrchestrationContext`
- `ReplaySafeLoggerFactory` exists specifically for wrapper and delegation scenarios like this one
- The wrapper shown here forwards the core abstract members needed by the sample; real wrappers can forward additional members as needed
