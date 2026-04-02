# Per-Orchestrator Versioning Sample

This sample demonstrates per-orchestrator versioning with `[DurableTaskVersion]`, where multiple implementations of the same logical orchestration name coexist in one worker process.

## What it shows

- Two classes share the same `[DurableTask("OrderWorkflow")]` name but have different `[DurableTaskVersion]` values
- The source generator produces version-qualified helpers like `ScheduleNewOrderWorkflow_1InstanceAsync()` and `ScheduleNewOrderWorkflow_2InstanceAsync()`
- `AddAllGeneratedTasks()` registers both versions automatically
- Each instance is routed to the correct implementation based on its version

## Prerequisites

- .NET 8.0 or 10.0 SDK
- [Docker](https://www.docker.com/get-started)

## Running the Sample

### 1. Start the DTS emulator

```bash
docker run --name durabletask-emulator -d -p 8080:8080 -e ASPNETCORE_URLS=http://+:8080 mcr.microsoft.com/dts/dts-emulator:latest
```

### 2. Set the connection string

```bash
export DURABLE_TASK_SCHEDULER_CONNECTION_STRING="Endpoint=http://localhost:8080;TaskHub=default;Authentication=None"
```

### 3. Run the sample

```bash
dotnet run
```

Expected output:

```
=== Per-orchestrator versioning ([DurableTaskVersion]) ===

Scheduling OrderWorkflow v1 ...
  Result: Order total: $50 (v1 — no discount)

Scheduling OrderWorkflow v2 ...
  Result: Order total: $40 (v2 — with discount)

Done! Both versions ran in the same worker process.
```

### 4. Clean up

```bash
docker rm -f durabletask-emulator
```

## When to use this approach

Per-orchestrator versioning is useful when:

- You need multiple versions of the same orchestration active simultaneously
- You want version-specific routing without deploying separate workers
- You're building a system where callers choose which version to invoke

For simpler deployment-based versioning, see the [WorkerVersioningSample](../WorkerVersioningSample/README.md).

> **Note:** Do not combine `[DurableTaskVersion]` routing with worker-level `UseVersioning()` in the same worker. Both features use the orchestration instance version field.
