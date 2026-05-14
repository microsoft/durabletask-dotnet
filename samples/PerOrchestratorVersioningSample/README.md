# Per-Orchestrator Versioning Sample

This sample demonstrates per-orchestrator versioning with `[DurableTask(Version = "...")]`, where multiple implementations of the same logical orchestration name coexist in one worker process.

## What it shows

- Two classes share the same `[DurableTask("OrderWorkflow", Version = "...")]` declarations with different `Version` values
- The source generator produces version-qualified helpers like `ScheduleNewOrderWorkflowV1InstanceAsync()` and `ScheduleNewOrderWorkflowV2InstanceAsync()`
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
=== Per-orchestrator versioning ([DurableTask] Version) ===

Scheduling OrderWorkflow v1 ...
  Result: Order total: $50 (v1 — no discount)

Scheduling OrderWorkflow v2 ...
  Result: Order total: $40 (v2 — with discount)

Done! Both versions ran in the same worker process.

Scheduling MigratingWorkflow v1 → v2 (ContinueAsNew migration) ...
  Result: Migrated order total: $80 (v2 — after migration from v1)

Sample completed successfully!
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

> **Note:** `[DurableTask(Version = "...")]` routing and worker-level `UseVersioning()` now compose. `UseVersioning()` filters which instance versions the worker accepts (via `MatchStrategy`), and the per-class registry dispatches each accepted work item to the implementation that exactly matches its `(name, version)`. Use them together if you need both deployment-level gating and same-process multi-version routing.
