// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Serializable outcome of exactly one attempted large-payload blob deletion. Mirrors the
/// <c>LargePayloadPurgeResult</c> protobuf message but is safe to pass through the orchestration/activity
/// boundary. The backend owns retry scheduling: it deletes rows reported as
/// <see cref="LargePayloadPurgeDisposition.Deleted"/>, reschedules
/// <see cref="LargePayloadPurgeDisposition.Retry"/> with a reason-appropriate next attempt, and moves
/// <see cref="LargePayloadPurgeDisposition.Quarantined"/> rows out of the active fetch.
/// </summary>
/// <param name="PartitionId">The backend partition that owns the tombstoned row.</param>
/// <param name="InstanceKey">The orchestration instance key the payload belonged to.</param>
/// <param name="PayloadId">The backend identifier of the tombstoned payload row.</param>
/// <param name="Revision">
/// The revision echoed unmodified from the fetched <see cref="LargePayloadTombstone"/>; used by the backend
/// as a compare-and-swap guard.
/// </param>
/// <param name="Disposition">The disposition of the deletion attempt.</param>
/// <param name="Reason">The stable reason code explaining the disposition.</param>
/// <param name="StorageErrorCode">
/// An optional bounded, sanitized storage status or error code for diagnostics (for example
/// <c>BlobNotFound</c> or <c>409</c>). Never contains a token or raw exception text.
/// </param>
public sealed record LargePayloadPurgeResult(
    int PartitionId,
    long InstanceKey,
    long PayloadId,
    long Revision,
    LargePayloadPurgeDisposition Disposition,
    LargePayloadPurgeReason Reason,
    string? StorageErrorCode = null);
