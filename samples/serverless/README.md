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
After pushing the remote worker image, set `ContainerImage` in
`main-app/WorkerProfiles.cs` to the pushed image reference. The same profile
class declares the remote activity name, CPU, memory, max concurrency, and a
customer environment variable that the remote hello activity echoes.

```powershell
$env:DTS_ENDPOINT = "https://<scheduler-endpoint>"
$env:DTS_TASK_HUB = "<task-hub>"
$env:DTS_SAMPLE_HELLO_INPUT = "serverless-sample"

dotnet run --project .\samples\serverless\main-app\main-app.csproj
```

Expected output includes the serverless activity result:

```text
Runtime status: Completed
Output: "hello locally: serverless-sample; hello remotely from <sandbox> pid=<pid>: serverless-sample; marker=serverless-dotnet-sample-marker"
```

Use the Durable Task Scheduler dashboard's Serverless Activities preview tab to inspect serverless activity runtimes and stream runtime logs.

The remote worker image does not need customer-provided DTS runtime settings.
DTS injects the scheduler endpoint, task hub, worker profile, capacity, substrate,
and sandbox identifier when it starts the sandbox. The worker reports the
activities registered in the image when it connects.
