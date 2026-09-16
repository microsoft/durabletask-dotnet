// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Log messages for the Azure Blob externalized-payload auto-purge job.
/// </summary>
static partial class Logs
{
    [LoggerMessage(EventId = 813, Level = LogLevel.Warning, Message = "Blob payload auto-purge quarantined a payload; cause '{cause}', storage code '{storageCode}'. The failure is deterministic and cannot succeed on a retry. The backend preserves the tombstone row and its token as evidence and stops polling it. The reported result carries the disposition alone, so this log is the only record of the cause.")]
    public static partial void BlobPurgeDeleteQuarantined(this ILogger logger, string cause, string? storageCode);

    [LoggerMessage(EventId = 814, Level = LogLevel.Debug, Message = "Blob payload auto-purge fetched {count} tombstoned payload(s) from the backend.")]
    public static partial void BlobPurgeFetchedTombstones(this ILogger logger, int count);

    [LoggerMessage(EventId = 815, Level = LogLevel.Debug, Message = "Blob payload auto-purge reported {count} purge result(s) to the backend.")]
    public static partial void BlobPurgeReportedResults(this ILogger logger, int count);

    [LoggerMessage(EventId = 819, Level = LogLevel.Warning, Message = "Blob payload auto-purge could not delete a payload; cause '{cause}', storage code '{storageCode}'. The backend reschedules the tombstone for a later attempt. The reported result carries the disposition alone, so this log is the only record of the cause.")]
    public static partial void BlobPurgeDeleteRetryable(this ILogger logger, string cause, string? storageCode);

    [LoggerMessage(EventId = 820, Level = LogLevel.Warning, Message = "Blob payload auto-purge cycle for job '{jobId}' failed; backing off before retrying so the job keeps running.")]
    public static partial void BlobPurgeCycleFailed(this ILogger logger, Exception exception, string? jobId);

    [LoggerMessage(EventId = 821, Level = LogLevel.Warning, Message = "An externalized payload blob does not carry this store's ownership marker, so it was left untouched; the tombstone is still resolved. This is expected for payloads written before the marker shipped, and for blobs the store never created whose token text matches the payload token grammar.")]
    public static partial void BlobPurgeBlobNotStoreOwned(this ILogger logger);

    [LoggerMessage(EventId = 830, Level = LogLevel.Error, Message = "Blob payload auto-purge runner '{jobId}' is waiting because the backend does not implement the large-payload purge RPCs: {detail}. Upgrade the backend, then explicitly enable auto-purge to wake the runner.")]
    public static partial void BlobPurgeBackendUnsupported(this ILogger logger, string? jobId, string detail);

    // Deliberately generic about WHICH precondition. The backend answers FailedPrecondition for more than one
    // condition - auto-purge being disabled for the task hub, and the task hub being deleted - and the server's
    // detail is what tells them apart, so it is carried through verbatim rather than being classified here.
    [LoggerMessage(EventId = 831, Level = LogLevel.Information, Message = "Blob payload auto-purge fetch was declined by the backend because a precondition is not met: {detail}. No blobs are deleted from this response; the job will retry after the normal idle delay.")]
    public static partial void BlobPurgeFetchPreconditionFailed(this ILogger logger, string detail);
}
