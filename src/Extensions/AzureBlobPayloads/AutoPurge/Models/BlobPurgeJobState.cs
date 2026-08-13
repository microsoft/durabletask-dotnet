// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// State for the singleton blob payload auto-purge job, stored in the entity.
/// </summary>
public sealed class BlobPurgeJobState
{
    /// <summary>
    /// Gets or sets the current status of the auto-purge job.
    /// </summary>
    public BlobPurgeJobStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the time when the job was first created.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the time of the last meaningful change to the job: when it was started, when it was
    /// stopped, when it was given a <see cref="PurgeBatchSize"/> different from the one it already had, or
    /// when it last recorded a non-zero number of purged blobs.
    /// </summary>
    /// <remarks>
    /// This is not a liveness or heartbeat signal, and it must not be read as one. Neither starting a host nor
    /// a reconciliation pass moves it, and an active job whose cycles keep finding nothing to purge leaves it
    /// untouched indefinitely, so a value far in the past is equally consistent with a healthy idle job and a
    /// dead one.
    /// </remarks>
    public DateTimeOffset? LastModifiedAt { get; set; }

    /// <summary>
    /// Gets or sets the total number of payload blobs the job has purged.
    /// </summary>
    public long PurgedCount { get; set; }

    /// <summary>
    /// Gets or sets the last error message, if any.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of tombstoned payloads requested from the backend per cycle.
    /// </summary>
    public int PurgeBatchSize { get; set; }
}
