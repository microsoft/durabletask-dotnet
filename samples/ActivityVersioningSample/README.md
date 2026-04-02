# Activity Versioning Sample

This sample demonstrates activity versioning with `[DurableTaskVersion]`, where multiple implementations of the same logical activity name coexist in one worker process and can be selected either by the orchestration instance version or by an explicit version-qualified helper.

## What it shows

- Two classes share the same `[DurableTask("ShippingQuote")]` name but have different `[DurableTaskVersion]` values
- Two versions of `CheckoutWorkflow` call the same logical activity name in one worker process using the default inherited-routing behavior
- The orchestration instance version is still the default for activity scheduling, so `CheckoutWorkflow` v1 routes to `ShippingQuote` v1 and `CheckoutWorkflow` v2 routes to `ShippingQuote` v2
- Version-qualified activity helpers like `CallShippingQuote_1Async()` and `CallShippingQuote_2Async()` now explicitly select those versions when called from an orchestration
- A third orchestration demonstrates explicitly overriding a `v2` orchestration to call the `ShippingQuote` v1 helper
- `AddAllGeneratedTasks()` registers both orchestration and activity versions automatically

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

```text
=== Activity versioning ([DurableTaskVersion]) ===

Scheduling CheckoutWorkflow v1 ...
  Result: Workflow v1 -> activity v1 quote: $57 (flat $7 shipping)

Scheduling CheckoutWorkflow v2 ...
  Result: Workflow v2 -> activity v2 quote: $45 ($10 bulk discount + $5 shipping)

Scheduling CheckoutWorkflow v2 with explicit ShippingQuote v1 override ...
  Result: Workflow v2 explicit override -> activity v1 quote: $57 (flat $7 shipping)

Done! Both versions ran in the same worker process.
Default activity calls inherit the orchestration version, but versioned helpers can explicitly override it.
```

### 4. Clean up

```bash
docker rm -f durabletask-emulator
```

## When to use this approach

Activity versioning is useful when:

- You need orchestration and activity behavior to evolve together across versions
- You want multiple versions of the same logical activity active simultaneously in one worker
- You want activity routing to follow the orchestration instance version by default, with explicit opt-in overrides when needed

For deployment-based versioning, see the [WorkerVersioningSample](../WorkerVersioningSample/README.md). For the orchestration-focused version of this pattern, see the [PerOrchestratorVersioningSample](../PerOrchestratorVersioningSample/README.md).
