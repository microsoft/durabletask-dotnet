# Unversioned Fallback Sample

This sample demonstrates opt-in unversioned fallback for per-task versioning. It shows how one explicit versioned class can coexist with an unversioned catch-all implementation for versions that do not have their own `[DurableTask(Version = "...")]` registration.

## What it shows

- `SupportWorkflowLegacyV140` is registered as `[DurableTask(nameof(SupportWorkflow), Version = "1.4.0")]`.
- `SupportWorkflow` is registered without a version and acts as the current catch-all implementation.
- The worker enables `DurableTaskWorkerOptions.VersioningOptions.UnversionedFallback = WhenNoExactMatch`.
- `UseWorkItemFilters()` is enabled, so the generated filter must allow unmatched versions to reach the worker.
- A version `1.4.0` request dispatches to the explicit legacy class.
- A version `1.0` request has no exact registration, so it dispatches to the unversioned fallback class.

## Prerequisites

- .NET 10.0 SDK
- [Docker](https://www.docker.com/get-started)

## Running the Sample

### 1. Start the DTS emulator

```bash
docker run --name durabletask-emulator -d -p 8080:8080 -p 8082:8082 -e ASPNETCORE_URLS=http://+:8080 mcr.microsoft.com/dts/dts-emulator:latest
```

The emulator exposes the gRPC sidecar on port 8080 and the local dashboard on port 8082. After running the sample below, you can open the dashboard at <http://localhost:8082> to inspect the orchestrations and their versions.

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

```text
=== Unversioned fallback for versioned task dispatch ===

Scheduling SupportWorkflow version 1.4.0 ...
  Result: Legacy SupportWorkflow 1.4.0 handled version '1.4.0' for Contoso: BGP session down

Scheduling SupportWorkflow version 1.0 ...
  Result: Current SupportWorkflow handled version '1.0' for Contoso: BGP session down

Done! Version 1.4.0 used the explicit legacy class; version 1.0 used the unversioned fallback.
```

### 4. Clean up

```bash
docker rm -f durabletask-emulator
```

## Key takeaways

- Exact version matches always win. A `1.4.0` request dispatches to the `1.4.0` class, not the unversioned class.
- Unversioned fallback is opt-in. Without `WhenNoExactMatch`, a mixed unversioned plus versioned registration remains a closed set and unknown versions fail rather than falling back.
- Use this mode only when the unversioned implementation is compatible with the versions it may receive. Replaying existing histories against a different implementation can cause non-determinism or deserialization failures.
- `UseWorkItemFilters()` composes with this mode by allowing unmatched versions for logical names that have an unversioned catch-all registration.

## See also

- [EternalOrchestrationVersionMigrationSample](../EternalOrchestrationVersionMigrationSample/README.md) — multi-version orchestration dispatch and `ContinueAsNew(NewVersion = "...")` migration.
- [ActivityVersioningSample](../ActivityVersioningSample/README.md) — activity versioning with inherited defaults and explicit overrides.
- [WorkerVersioningSample](../WorkerVersioningSample/README.md) — worker-level deployment versioning via `UseVersioning()`.
