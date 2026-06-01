# On-demand sandbox activities sample

This sample shows how to run selected Durable Task activities in DTS-managed on-demand sandboxes.

The sample is intentionally split into two projects:

| Path | Purpose |
| --- | --- |
| `shared/` | Defines activity name constants shared by the main app and remote worker. |
| `main-app/` | Runs locally or in a normal app host. It declares the on-demand sandbox activity and starts one hello orchestration. |
| `remote-worker/` | Builds the container image that DTS starts inside a sandbox. It contains the remote hello activity. |

## Build

```powershell
dotnet build .\samples\on-demand-sandbox\main-app\main-app.csproj
dotnet build .\samples\on-demand-sandbox\remote-worker\remote-worker.csproj
```

## Build the remote worker image

Run from the repository root:

```powershell
$image = "<acr-name>.azurecr.io/dts-ondemand-sandbox-sample:<tag>"
docker build -f .\samples\on-demand-sandbox\remote-worker\Containerfile -t $image .
docker push $image
```

## Run a hello orchestration

The main app uses `DefaultAzureCredential`; sign in with Azure CLI or configure another supported Azure identity before running it.
After pushing the remote worker image, set `ContainerImage` in
`main-app/WorkerProfiles.cs` to the pushed image reference. The worker profile
class declares the image, CPU, memory, max concurrency, and on-demand sandbox activity
names with `options.AddActivity(...)`. The main app and remote worker both use
the `shared/ActivityNames.cs` constants so the declaration and worker registration
stay in sync.

Update `main-app/appsettings.json` with your scheduler endpoint and task hub:

```json
{
  "OnDemandSandboxSample": {
    "EndpointAddress": "https://<scheduler-endpoint>",
    "TaskHubName": "OnDemandSandboxPocHub"
  }
}
```

Then run the main app:

```powershell
Push-Location .\samples\on-demand-sandbox\main-app
dotnet run
Pop-Location
```

Expected output includes the on-demand sandbox activity result:

```text
Runtime status: Completed
Output: "hello locally: on-demand-sandbox-sample; hello remotely from <sandbox> pid=<pid>: on-demand-sandbox-sample"
```

Use the Durable Task Scheduler dashboard's On-demand sandbox preview tab to inspect sandboxes and stream runtime logs.

The remote worker image does not need customer-provided DTS runtime settings.
DTS injects the scheduler endpoint, task hub, worker profile, capacity, substrate,
and sandbox identifier when it starts the sandbox. The worker reports the
activities registered in the image when it connects.
