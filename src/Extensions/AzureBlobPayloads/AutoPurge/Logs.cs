// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Log messages for the Azure Blob externalized-payload auto-purge job.
/// </summary>
static partial class Logs
{
    [LoggerMessage(EventId = 810, Level = LogLevel.Information, Message = "Blob payload auto-purge job '{jobId}' created.")]
    public static partial void BlobPurgeJobCreated(this ILogger logger, string? jobId);

    [LoggerMessage(EventId = 811, Level = LogLevel.Information, Message = "Blob payload auto-purge job '{jobId}' is already running; ignoring the create request.")]
    public static partial void BlobPurgeJobAlreadyRunning(this ILogger logger, string? jobId);

    [LoggerMessage(EventId = 812, Level = LogLevel.Information, Message = "Blob payload auto-purge orchestrator for job '{jobId}' stopping; job status is {status}.")]
    public static partial void BlobPurgeJobOrchestratorStopping(this ILogger logger, string? jobId, string status);

    [LoggerMessage(EventId = 813, Level = LogLevel.Warning, Message = "Failed to delete externalized payload blob for token '{token}'; leaving it tombstoned for a later purge cycle.")]
    public static partial void BlobPurgeDeleteFailed(this ILogger logger, Exception exception, string token);

    [LoggerMessage(EventId = 814, Level = LogLevel.Debug, Message = "Blob payload auto-purge fetched {count} tombstoned payload(s) from the backend.")]
    public static partial void BlobPurgeFetchedTombstones(this ILogger logger, int count);

    [LoggerMessage(EventId = 815, Level = LogLevel.Debug, Message = "Blob payload auto-purge acknowledged {count} purged payload(s) to the backend.")]
    public static partial void BlobPurgeAckedPayloads(this ILogger logger, int count);

    [LoggerMessage(EventId = 817, Level = LogLevel.Information, Message = "Blob payload auto-purge singleton job ensured.")]
    public static partial void BlobPurgeJobEnsured(this ILogger logger);

    [LoggerMessage(EventId = 818, Level = LogLevel.Warning, Message = "Blob payload auto-purge starter could not ensure the singleton job; retrying.")]
    public static partial void BlobPurgeStarterRetry(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 819, Level = LogLevel.Warning, Message = "Discarding poison externalized payload token '{token}'; it can never be deleted, acknowledging it so the backend can clear the row.")]
    public static partial void BlobPurgeDeleteDiscarded(this ILogger logger, Exception exception, string token);

    [LoggerMessage(EventId = 820, Level = LogLevel.Warning, Message = "Blob payload auto-purge cycle for job '{jobId}' failed; backing off before retrying so the job keeps running.")]
    public static partial void BlobPurgeCycleFailed(this ILogger logger, Exception exception, string? jobId);

    [LoggerMessage(EventId = 821, Level = LogLevel.Error, Message = "Externalized payload token '{token}' points at a storage account the configured credential cannot reach; the blob cannot be deleted by this worker and will be orphaned. Acknowledging it so the backend can clear the row - reclaim the blob manually or reconfigure the payload store with identity (AAD) authentication that can access both accounts.")]
    public static partial void BlobPurgeDeleteUnreachable(this ILogger logger, Exception exception, string token);

    [LoggerMessage(EventId = 822, Level = LogLevel.Error, Message = "Received a legacy v1 externalized payload token '{token}' from the backend, which is unexpected: a current backend hard-deletes v1 payload rows instead of tombstoning them, so this indicates either an older backend build or a row that was tombstoned before that fix. Auto-purge does not delete v1 tokens because a v1 token identifies the container by name only and not the storage account, so the delete cannot be verified; the backing blob is NOT deleted and the row is acknowledged so the purge pipeline is not blocked. Reclaim the blob using the container and blob name in the token above, and upgrade to an SDK version that writes self-describing v2 tokens.")]
    public static partial void BlobPurgeDeleteV1TokenUnsupported(this ILogger logger, string token);

    [LoggerMessage(EventId = 823, Level = LogLevel.Error, Message = "Blob payload auto-purge is enabled but the registered PayloadStore ('{storeType}') is not an Azure Blob payload store and cannot delete payloads. The auto-purge job was not started; externalized payloads will not be reclaimed. Register the Azure Blob payload store, or disable AutoPurge.")]
    public static partial void BlobPurgeStoreCannotDelete(this ILogger logger, string? storeType);

    [LoggerMessage(EventId = 824, Level = LogLevel.Error, Message = "The registered PayloadStore does not support deleting payloads, so externalized payload token '{token}' cannot be purged. Leaving it tombstoned; register an Azure Blob payload store on the worker or disable AutoPurge.")]
    public static partial void BlobPurgeDeleteNotSupported(this ILogger logger, Exception exception, string token);
}
