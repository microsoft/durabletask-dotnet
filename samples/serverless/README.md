# Serverless Activities Sample

This sample shows how to run selected Durable Task activities in DTS-managed serverless sandboxes.

The sample is intentionally split into two projects:

| Path | Purpose |
| --- | --- |
| `main-app/` | Runs locally or in a normal app host. It declares the serverless activity and starts one hello orchestration. |
| `remote-worker/` | Builds the container image that DTS starts inside a serverless sandbox. It contains the remote hello activity. |

## Build

```powershell
dotnet build .\samples\serverless\main-app\main-app.csproj
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

The main app uses `DefaultAzureCredential`; sign in with Azure CLI or configure another supported Azure identity before running it.

```powershell
$env:DTS_ENDPOINT = "https://<scheduler-endpoint>"
$env:DTS_TASK_HUB = "<task-hub>"
$env:DTS_SERVERLESS_ACTIVITY_IMAGE = "<acr-name>.azurecr.io/dts-serverless-sample:<tag>"
$env:DTS_SERVERLESS_CPU = "1000m"
$env:DTS_SERVERLESS_MEMORY = "2048Mi"
$env:DTS_SERVERLESS_MAX_ACTIVITIES = "1"
$env:DTS_SAMPLE_HELLO_INPUT = "serverless-sample"

dotnet run --project .\samples\serverless\main-app\main-app.csproj
```

Expected output includes the serverless activity result:

```text
Runtime status: Completed
Output: "hello from <sandbox> pid=<pid>: serverless-sample"
```

Use the Durable Task Scheduler dashboard's Serverless Activities preview tab to inspect serverless activity runtimes and stream runtime logs.

