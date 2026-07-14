// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Represents the current status of the singleton blob payload auto-purge job.
/// </summary>
public enum BlobPurgeJobStatus
{
    /// <summary>
    /// The job has not been started yet. This is the default status of a freshly initialized entity, so it is
    /// kept as the zero value to avoid a brand-new entity accidentally appearing active.
    /// </summary>
    Pending,

    /// <summary>
    /// The job is active and draining tombstoned payloads from the backend.
    /// </summary>
    Active,
}
