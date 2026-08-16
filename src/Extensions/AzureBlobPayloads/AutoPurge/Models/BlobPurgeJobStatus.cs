// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Represents the current status of the singleton blob payload auto-purge job.
/// </summary>
public enum BlobPurgeJobStatus
{
    /// <summary>
    /// The job is not running. This is both the state of a job that has never been started and the resting
    /// state of one that has been stopped, which <see cref="BlobPurgeJobState.CreatedAt"/> distinguishes: it is
    /// null only for a job that was never created. It is kept as the zero value so a brand-new entity does not
    /// accidentally appear active.
    /// </summary>
    Pending,

    /// <summary>
    /// The job is active and draining tombstoned payloads from the backend.
    /// </summary>
    Active,

    /// <summary>
    /// The backend does not implement the large-payload purge RPCs, so the job cannot run. This happens against
    /// an older backend build or a stale local emulator image that predates the feature. It is kept distinct
    /// from <see cref="Pending"/> precisely because <see cref="BlobPurgeJob.Create"/> must NOT re-activate it:
    /// the client-side starter re-issues <c>Create</c> on every reconciliation pass, so a merely-stopped
    /// (<see cref="Pending"/>) job is meant to come back, whereas an unsupported one must stay down until the
    /// process restarts and re-checks the backend.
    /// </summary>
    Unsupported = 2,
}
