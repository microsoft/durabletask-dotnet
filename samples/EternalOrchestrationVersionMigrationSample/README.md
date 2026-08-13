# Eternal Orchestration Version Migration Sample

This sample demonstrates the **eternal-orchestration migration** scenario for `[DurableTask(Version = "...")]`: a long-running orchestration ships its current implementation as v1, and when the logic needs to change, a new v2 class is shipped alongside v1 and in-flight instances migrate themselves to v2 via `ContinueAsNew(NewVersion = "2")`.

The sample also illustrates the underlying multi-version dispatch mechanism with a simpler `OrderWorkflow` pair (v1 and v2 of the same orchestration coexisting in one worker), so you can see the routing on its own before the migration story is layered on top.

## What it shows

1. **Multi-version dispatch** — `OrderWorkflow` v1 and v2 are both registered. Each new instance is stamped with a specific version and the worker routes it to the matching implementation. The source generator produces `ScheduleNewOrderWorkflowV1InstanceAsync()` and `ScheduleNewOrderWorkflowV2InstanceAsync()` helpers that stamp the version automatically.
2. **Eternal-orchestration migration** — `MigratingWorkflow` v1 contains the original logic plus a single-line edit on its terminating `ContinueAsNew` call: `new ContinueAsNewOptions { NewVersion = "2", NewInput = totalJobs }`. In-flight v1 instances complete their current turn, then restart as v2 at the `ContinueAsNew` boundary. v2 implements the corrected logic.

This is the recommended migration pattern for orchestrations that:

- Cannot be terminated (they hold or update state your system depends on)
- Cannot be re-deployed in place by editing the existing class (that would break replay determinism for in-flight instances)

`ContinueAsNew(NewVersion = "...")` is the replay-safe migration boundary: history is fully reset at the boundary, so the v2 implementation isn't replayed against v1's history.

## Prerequisites

- .NET 10.0 SDK
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

PowerShell:

```powershell
$env:DURABLE_TASK_SCHEDULER_CONNECTION_STRING = "Endpoint=http://localhost:8080;TaskHub=default;Authentication=None"
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

- You have an existing orchestration in production and need to change its logic without losing in-flight instances.
- You need multiple versions of the same orchestration active simultaneously (existing instances continue on the old version, new instances start on the new version, and in-flight instances migrate at a clean boundary).
- You want version-specific routing without deploying separate worker pools per version.

For state that needs to survive the `ContinueAsNew(NewVersion = "...")` boundary, pass it as the new orchestration input. If the state is shared with other parties, anchor it in a durable entity — see [EntityWithVersionedOrchestrationSample](../EntityWithVersionedOrchestrationSample/README.md).

For simpler deployment-based versioning where the whole worker is pinned to a single version, see [WorkerVersioningSample](../WorkerVersioningSample/README.md).

> [!NOTE]
> `[DurableTask(Version = "...")]` routing and worker-level `UseVersioning()` compose. `UseVersioning()` filters which instance versions the worker accepts (via `MatchStrategy`), and the per-class registry dispatches each accepted work item to the implementation that exactly matches its `(name, version)`. Use them together if you need both deployment-level gating and same-process multi-version routing.
