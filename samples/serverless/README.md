# Serverless Activities Sample

This sample shows how to run selected Durable Task activities in DTS-managed serverless sandboxes.

The sample is intentionally split into two projects:

| Path | Purpose |
| --- | --- |
| `declarer/` | Runs locally or in a normal app host. It declares the serverless activities, runs local orchestrations and local activities, and can expose HTTP helpers for listing sandboxes and streaming logs. |
| `remote-worker/` | Builds the container image that DTS starts inside a serverless sandbox. It contains only the remote activities. |

## Build

```powershell
dotnet build .\samples\serverless\declarer\declarer.csproj
dotnet build .\samples\serverless\remote-worker\remote-worker.csproj
```

## Build the remote worker image

Run from the repository root:

```powershell
$image = "<acr-name>.azurecr.io/dts-serverless-sample:<tag>"
docker build -f .\samples\serverless\remote-worker\Containerfile -t $image .
docker push $image
```

## Run a hello orchestration

The declarer uses `DefaultAzureCredential`; sign in with Azure CLI or configure another supported Azure identity before running it.

```powershell
$env:DTS_ENDPOINT = "https://<scheduler-endpoint>"
$env:DTS_TASK_HUB = "<task-hub>"
$env:DTS_SERVERLESS_ACTIVITY_IMAGE = "<acr-name>.azurecr.io/dts-serverless-sample:<tag>"
$env:DTS_SERVERLESS_CPU = "1000m"
$env:DTS_SERVERLESS_MEMORY = "2048Mi"
$env:DTS_SERVERLESS_MAX_ACTIVITIES = "1"

dotnet run --project .\samples\serverless\declarer\declarer.csproj -- hello serverless-sample
```

Expected output includes both a local activity result and a serverless activity result:

```text
Runtime status: Completed
Output: "local:serverless-sample | hello from <sandbox> pid=<pid>: serverless-sample"
```

## Sandbox log helper API

The declarer can also expose a small HTTP helper API. The helper reuses the SDK's DTS serverless client registration instead of setting up gRPC channels directly.

```powershell
dotnet run --project .\samples\serverless\declarer\declarer.csproj -- serve
```

Endpoints:

- `GET /health`
- `GET /serverless/sandboxes?workerProfileId=default`
- `GET /serverless/sandboxes/{dtsSandboxIdentifier}/logs?tail=100`
