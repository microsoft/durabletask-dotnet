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

    [LoggerMessage(EventId = 813, Level = LogLevel.Warning, Message = "Blob payload auto-purge quarantined a payload; cause '{cause}', storage code '{storageCode}'. The failure is deterministic and cannot succeed on a retry. The backend preserves the tombstone row and its token as evidence and stops polling it. The reported result carries the disposition alone, so this log is the only record of the cause.")]
    public static partial void BlobPurgeDeleteQuarantined(this ILogger logger, string cause, string? storageCode);

    [LoggerMessage(EventId = 814, Level = LogLevel.Debug, Message = "Blob payload auto-purge fetched {count} tombstoned payload(s) from the backend.")]
    public static partial void BlobPurgeFetchedTombstones(this ILogger logger, int count);

    [LoggerMessage(EventId = 815, Level = LogLevel.Debug, Message = "Blob payload auto-purge reported {count} purge result(s) to the backend.")]
    public static partial void BlobPurgeReportedResults(this ILogger logger, int count);

    [LoggerMessage(EventId = 816, Level = LogLevel.Information, Message = "Blob payload auto-purge job '{jobId}' stopped. The perpetual orchestrator is not terminated; it reads the job state at the start of its next cycle and exits on its own.")]
    public static partial void BlobPurgeJobStopped(this ILogger logger, string? jobId);

    [LoggerMessage(EventId = 817, Level = LogLevel.Information, Message = "Blob payload auto-purge singleton job ensured.")]
    public static partial void BlobPurgeJobEnsured(this ILogger logger);

    [LoggerMessage(EventId = 818, Level = LogLevel.Warning, Message = "Blob payload auto-purge starter could not reach the singleton job; retrying.")]
    public static partial void BlobPurgeStarterRetry(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 819, Level = LogLevel.Warning, Message = "Blob payload auto-purge could not delete a payload; cause '{cause}', storage code '{storageCode}'. The backend reschedules the tombstone for a later attempt. The reported result carries the disposition alone, so this log is the only record of the cause.")]
    public static partial void BlobPurgeDeleteRetryable(this ILogger logger, string cause, string? storageCode);

    [LoggerMessage(EventId = 820, Level = LogLevel.Warning, Message = "Blob payload auto-purge cycle for job '{jobId}' failed; backing off before retrying so the job keeps running.")]
    public static partial void BlobPurgeCycleFailed(this ILogger logger, Exception exception, string? jobId);

    [LoggerMessage(EventId = 821, Level = LogLevel.Warning, Message = "An externalized payload blob does not carry this store's ownership marker, so it was left untouched; the tombstone is still resolved. This is expected for payloads written before the marker shipped, and for blobs the store never created whose token text matches the payload token grammar.")]
    public static partial void BlobPurgeBlobNotStoreOwned(this ILogger logger);

    [LoggerMessage(EventId = 822, Level = LogLevel.Debug, Message = "Blob payload auto-purge job '{jobId}' is already stopped; ignoring the stop request.")]
    public static partial void BlobPurgeJobAlreadyStopped(this ILogger logger, string? jobId);

    [LoggerMessage(EventId = 823, Level = LogLevel.Error, Message = "Blob payload auto-purge is enabled but the registered PayloadStore ('{storeType}') is not an Azure Blob payload store and cannot delete payloads. The auto-purge job was not started; externalized payloads will not be reclaimed. Register the Azure Blob payload store, or disable AutoPurge.")]
    public static partial void BlobPurgeStoreCannotDelete(this ILogger logger, string? storeType);

    [LoggerMessage(EventId = 824, Level = LogLevel.Information, Message = "Blob payload auto-purge is disabled, so a stop was requested for the singleton job. A job left running by an earlier configuration exits after its current cycle.")]
    public static partial void BlobPurgeJobStopRequested(this ILogger logger);
}
