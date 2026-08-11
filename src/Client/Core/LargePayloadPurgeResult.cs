// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Serializable outcome of exactly one attempted large-payload blob deletion. Mirrors the
/// <c>LargePayloadPurgeResult</c> protobuf message but is safe to pass through the orchestration/activity
/// boundary. The backend owns retry scheduling and branches solely on
/// <see cref="Disposition"/>: it deletes rows reported as
/// <see cref="LargePayloadPurgeDisposition.Deleted"/>, reschedules
/// <see cref="LargePayloadPurgeDisposition.Retry"/> on its own backoff, and moves
/// <see cref="LargePayloadPurgeDisposition.Quarantined"/> rows out of the active fetch. The worker never
/// computes a retry delay.
/// </summary>
/// <remarks>
/// The disposition is deliberately the only outcome field: anything finer would be write-only on the backend.
/// Why an attempt failed stays in the worker's own telemetry, which holds the cause at full fidelity rather
/// than as a lossy classification, and a row is correlated to it by
/// (<see cref="PartitionId"/>, <see cref="InstanceKey"/>, <see cref="PayloadId"/>).
/// </remarks>
/// <param name="PartitionId">The backend partition that owns the tombstoned row.</param>
/// <param name="InstanceKey">The orchestration instance key the payload belonged to.</param>
/// <param name="PayloadId">The backend identifier of the tombstoned payload row.</param>
/// <param name="Revision">
/// The revision echoed unmodified from the fetched <see cref="LargePayloadTombstone"/>; used by the backend
/// as a compare-and-swap guard.
/// </param>
/// <param name="Disposition">The disposition of the deletion attempt.</param>
public sealed record LargePayloadPurgeResult(
    int PartitionId,
    long InstanceKey,
    long PayloadId,
    long Revision,
    LargePayloadPurgeDisposition Disposition);
