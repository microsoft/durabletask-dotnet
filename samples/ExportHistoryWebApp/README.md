# Export History Web App Sample

This sample is a small ASP.NET Core web app that exposes a REST API for creating and managing Durable Task **export history jobs**.

It uses:

- **Durable Task Scheduler (Azure Managed)** for listing instance history
- **Azure Blob Storage** as the export destination for exported orchestration history

## Prerequisites

- A Durable Task Scheduler hub connection string
- An Azure Storage account connection string (or Azurite/Storage Emulator)

## Configure

The app reads configuration from standard .NET configuration sources (environment variables, `appsettings*.json`, command-line, etc.).

Required settings:

- `DURABLE_TASK_CONNECTION_STRING`
  - Durable Task Scheduler connection string for your task hub.
- `EXPORT_HISTORY_STORAGE_CONNECTION_STRING`
  - Azure Storage connection string used for writing exported history.
- `EXPORT_HISTORY_CONTAINER_NAME`
  - Default blob container name for export output.

Optional settings:

- `EXPORT_HISTORY_PREFIX`
  - Default blob “folder” prefix used when writing blobs.

## Run

From the repo root:

```bash
dotnet run --project samples/ExportHistoryWebApp/ExportHistoryWebApp.csproj
```

The default `launchSettings.json` profile listens on:

- `http://localhost:5009`

## Interact with the export API

The controller is rooted at `export-jobs` and supports create/get/list/delete.

### Create an export job

`POST /export-jobs`

Request body (see `Models/CreateExportJobRequest.cs`):

- `jobId` (optional): If omitted, a GUID is generated.
- `mode`: `Batch` or `Continuous`.
- `completedTimeFrom`: Start of the export time window (inclusive).
- `completedTimeTo`:
  - Required for `Batch`
  - Must be omitted/null for `Continuous`
- `container` / `prefix` (optional): Overrides the default destination configured in app settings.
- `runtimeStatus` (optional): Filters exported instances by terminal status.
  - Allowed values: `Completed`, `Failed`, `Terminated`
- `maxInstancesPerBatch` (optional): 1–1000 (defaults to 100).
- `format` (optional): Defaults to JSONL + gzip.

Notes:
- For `Batch` mode, `completedTimeTo` must be greater than `completedTimeFrom` and cannot be in the future.

### Get a job

`GET /export-jobs/{id}`

Returns an `ExportJobDescription` if the job exists.

### List jobs

`GET /export-jobs/list`

Optional query parameters:

- `status`: `Active`, `Failed`, `Completed`
- `jobIdPrefix`
- `createdFrom`, `createdTo`
- `pageSize`, `continuationToken`

### Delete a job

`DELETE /export-jobs/{id}`

## Where exported data goes

Exported history is written to Azure Blob Storage:

- Container: default from `EXPORT_HISTORY_CONTAINER_NAME` (or per-request `container` override)
- Blob name: derived from a SHA-256 hash of `(completedTimestamp, instanceId)`
- File extension:
  - Default: `.jsonl.gz` (JSON Lines, gzip-compressed)
  - Optional: `.json` (if configured via `format`)

If a prefix is configured, the blob path becomes:

- `{prefix}/{hash}.{ext}`

## Using the included HTTP file

This sample includes ready-made requests in `ExportHistoryWebApp.http`.

In VS Code:

1. Install the “REST Client” extension (if you don’t already have it).
2. Open `ExportHistoryWebApp.http`.
3. Click “Send Request” on any request block.

Adjust the `@baseUrl` variable if you run the app on a different port.
