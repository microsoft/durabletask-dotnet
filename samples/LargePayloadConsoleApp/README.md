# Large Payload Externalization Sample

This sample demonstrates configuring Durable Task to externalize large payloads to Azure Blob Storage using `UseExternalizedPayloads` on both client and worker, connecting via Durable Task Scheduler (no local sidecar).

- Defaults to Azurite/Storage Emulator via `UseDevelopmentStorage=true`.
- Threshold is set to 1KB for demo, so even modest inputs are externalized.

## Prerequisites

- A Durable Task Scheduler connection string (e.g., from Azure portal) in `DURABLE_TASK_SCHEDULER_CONNECTION_STRING`.
- Optional: Run Azurite (if not using real Azure Storage) for payload storage tokens.

## Configure

Environment variables (optional):

- `DURABLETASK_STORAGE`: Azure Storage connection string. Defaults to `UseDevelopmentStorage=true`.
- `DURABLETASK_PAYLOAD_CONTAINER`: Blob container name. Defaults to `durabletask-payloads`.

The sample keeps the default access scope: payload downloads and deletion are limited
to the configured storage account endpoint and container. Explicit account-wide
and additional-account options, authentication requirements, and migration behavior
are described in the [payload access scope documentation](../../src/Extensions/AzureBlobPayloads/README.md).

## Run

```bash
# from repo root
dotnet run --project samples/LargePayloadConsoleApp/LargePayloadConsoleApp.csproj
```

The app starts an orchestration with a 1MB input, which is externalized by the client and resolved by the worker. The console shows a token-like serialized input and a deserialized input length.


