# Worker-Level Versioning Sample

This sample demonstrates worker-level versioning, where each worker deployment is associated with a single version string.

## What it shows

- The client uses `UseDefaultVersion()` to stamp every new orchestration instance with a version
- The orchestration reads `context.Version` to see what version it was scheduled with
- To "upgrade," you redeploy the worker with a new implementation and change the version string

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

### 3. Run with version 1.0 (default)

```bash
dotnet run
```

### 4. Simulate a deployment upgrade to version 2.0

```bash
WORKER_VERSION=2.0 dotnet run
```

### 5. Clean up

```bash
docker rm -f durabletask-emulator
```

## When to use this approach

Worker-level versioning is the simplest model. Use it when:

- You deploy one version of your orchestration logic at a time
- You want a straightforward rolling upgrade story
- You don't need multiple versions of the same orchestration active simultaneously

For running multiple versions of the same orchestration in one worker, see the [PerOrchestratorVersioningSample](../PerOrchestratorVersioningSample/README.md).
