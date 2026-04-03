# Distributed Tracing with OpenTelemetry Sample

This sample demonstrates how to configure OpenTelemetry distributed tracing with the Durable Task SDK. Traces are exported to Jaeger via OTLP, allowing you to visualize the full execution flow of orchestrations and activities.

## Overview

The Durable Task SDK automatically emits traces using the `Microsoft.DurableTask` [ActivitySource](https://learn.microsoft.com/dotnet/api/system.diagnostics.activitysource). This sample subscribes to that source using the OpenTelemetry SDK and exports traces to Jaeger using the OTLP exporter.

The sample runs a **fan-out/fan-in** orchestration that:
1. Fans out to 5 parallel `GetWeather` activity calls (one per city)
2. Fans in by waiting for all activities to complete
3. Calls a `CreateSummary` activity to aggregate the results

This pattern produces a rich trace with multiple parallel spans, making it easy to visualize in Jaeger.

## Prerequisites

- .NET 10.0 SDK or later
- [Docker](https://www.docker.com/get-started) and [Docker Compose](https://docs.docker.com/compose/)

## Running the Sample

### 1. Start Jaeger and the DTS Emulator

From this directory, start the infrastructure containers:

```bash
docker compose up -d
```

This starts:
- **Jaeger** — UI at [http://localhost:16686](http://localhost:16686), OTLP receiver on ports 4317 (gRPC) and 4318 (HTTP)
- **DTS Emulator** — gRPC endpoint on port 8080, dashboard UI at [http://localhost:8082](http://localhost:8082)

### 2. Run the Sample

```bash
dotnet run
```

The sample connects to the DTS emulator by default using the connection string:
```
Endpoint=http://localhost:8080;Authentication=None;TaskHub=default
```

To override, set the `DURABLE_TASK_SCHEDULER_CONNECTION_STRING` environment variable:
```bash
export DURABLE_TASK_SCHEDULER_CONNECTION_STRING="Endpoint=http://localhost:8080;Authentication=None;TaskHub=default"
dotnet run
```

### 3. View Traces in Jaeger

1. Open the Jaeger UI at [http://localhost:16686](http://localhost:16686)
2. Select **DistributedTracingSample** from the "Service" dropdown
3. Click **Find Traces**
4. Click on a trace to see the full execution flow

You should see a trace with spans for:
- The `FanOutFanIn` orchestration
- Five parallel `GetWeather` activity executions
- A final `CreateSummary` activity execution

### 4. Clean Up

```bash
docker compose down
```

## How It Works

The key OpenTelemetry configuration is straightforward:

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("DistributedTracingSample"))
    .WithTracing(tracing =>
    {
        tracing.AddSource("Microsoft.DurableTask");
        tracing.AddOtlpExporter();
    });
```

- **`AddSource("Microsoft.DurableTask")`** subscribes to the SDK's built-in ActivitySource
- **`AddOtlpExporter()`** sends traces to the default OTLP endpoint (`http://localhost:4317`)
- **`AddService("DistributedTracingSample")`** sets the service name shown in Jaeger
