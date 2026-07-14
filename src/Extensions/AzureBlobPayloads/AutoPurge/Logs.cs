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

    [LoggerMessage(EventId = 816, Level = LogLevel.Information, Message = "Blob payload auto-purge is disabled; the singleton purge job will not be started.")]
    public static partial void BlobPurgeDisabled(this ILogger logger);

    [LoggerMessage(EventId = 817, Level = LogLevel.Information, Message = "Blob payload auto-purge singleton job ensured.")]
    public static partial void BlobPurgeJobEnsured(this ILogger logger);

    [LoggerMessage(EventId = 818, Level = LogLevel.Warning, Message = "Blob payload auto-purge starter could not ensure the singleton job; retrying.")]
    public static partial void BlobPurgeStarterRetry(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 819, Level = LogLevel.Warning, Message = "Discarding poison externalized payload token '{token}'; it can never be deleted, acknowledging it so the backend can clear the row.")]
    public static partial void BlobPurgeDeleteDiscarded(this ILogger logger, Exception exception, string token);

    [LoggerMessage(EventId = 820, Level = LogLevel.Warning, Message = "Blob payload auto-purge cycle for job '{jobId}' failed; backing off before retrying so the job keeps running.")]
    public static partial void BlobPurgeCycleFailed(this ILogger logger, Exception exception, string? jobId);
}
