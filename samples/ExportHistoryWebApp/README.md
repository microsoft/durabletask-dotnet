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
- `Continuous` mode pages through fixed completion-time windows, then resets the instance-key cursor for the next window. This allows earlier-created instances that complete after the current window's upper bound to be picked up in a subsequent window, provided they are query-visible when that window is scanned.
- Window upper bounds use the orchestrator's clock, not a server snapshot watermark. Delayed query visibility or clock skew can still leave an instance behind the pagination cursor or advanced completion-time bound, causing an export gap. If the backend does not guarantee timely query visibility and compatible timestamps, retain source histories and reconcile exports before purging; continuous export alone does not guarantee that every terminal instance has been exported.
- For example, an instance stamped as completed at `T - 1s` but not query-visible until after a window ending at `T` has been scanned is excluded from the next window starting at `T`. Equal completion timestamps do not resolve this race: a smaller creation key becoming visible after pagination has passed it can still be missed unless its completion time is on the shared window boundary. A settle delay or trailing overlap would only cover bounded visibility delay and clock skew, not establish an unconditional guarantee.
- Completion-time bounds are inclusive, so instances exactly on a window boundary can be exported again. Retries can also repeat exports; deterministic blob names target the same destination, subject to the versioning and immutability restrictions below. Progress counters count successful export operations, not distinct blobs.
- After upgrading the worker, existing continuous jobs adopt completion-time windows at their next continue-as-new (after at most five polling/page cycles). Legacy checkpoints restart from the job's original `completedTimeFrom` to recover previously skipped instances, which can re-export existing blobs. Keep source histories available until reconciliation finishes; already-purged histories cannot be recovered.

### Get a job

`GET /export-jobs/{id}`

Returns an `ExportJobDescription` if the job exists.

### List jobs

`GET /export-jobs/list`

Optional query parameters:

- `status`: `Pending`, `Active`, `Failed`, `Completed`
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

Repeated exports target the same blob name, but destination policies can make the effects non-idempotent: with blob versioning enabled, repeated writes can create additional versions and storage costs; an applicable immutability policy can prevent an overwrite and fail the export. Choose a destination policy compatible with retries, boundary duplicates, and migration re-exports, and do not interpret a stable blob name as a guarantee of exactly-once writes.

## Using the included HTTP file

This sample includes ready-made requests in `ExportHistoryWebApp.http`.

In VS Code:

1. Install the “REST Client” extension (if you don’t already have it).
2. Open `ExportHistoryWebApp.http`.
3. Click “Send Request” on any request block.

Adjust the `@baseUrl` variable if you run the app on a different port.
