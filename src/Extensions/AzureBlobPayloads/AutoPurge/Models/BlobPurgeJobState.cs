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
    /// Gets or sets the time when the job state was last modified.
    /// </summary>
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
