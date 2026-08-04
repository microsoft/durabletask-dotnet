// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// The outcome of attempting to delete a single externalized payload blob during an auto-purge cycle.
/// </summary>
public enum BlobDeleteResult
{
    /// <summary>
    /// The blob was deleted, or was already gone. The payload should be acknowledged so the backend can
    /// hard-delete the row.
    /// </summary>
    Deleted,

    /// <summary>
    /// The token is permanently invalid (poison) and can never be deleted. The payload should still be
    /// acknowledged so the backend clears the stuck row instead of re-streaming the same token forever.
    /// </summary>
    Discarded,

    /// <summary>
    /// A transient failure occurred. The payload is left tombstoned so a later purge cycle can retry it.
    /// </summary>
    Retry,
}
