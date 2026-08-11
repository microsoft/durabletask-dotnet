// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Serializable representation of a tombstoned large-payload row whose external blob a credentialed caller
/// must delete. Mirrors the <c>LargePayloadTombstone</c> protobuf message but is safe to pass through the
/// orchestration/activity boundary.
/// </summary>
/// <param name="PartitionId">The backend partition that owns the tombstoned row.</param>
/// <param name="InstanceKey">The orchestration instance key the payload belonged to.</param>
/// <param name="PayloadId">The backend identifier of the tombstoned payload row.</param>
/// <param name="Token">
/// The self-describing <c>blob:v2:{fullBlobUrl}</c> payload token whose backing blob should be deleted.
/// </param>
/// <param name="Revision">
/// An optimistic-concurrency guard echoed back unmodified in the corresponding
/// <see cref="LargePayloadPurgeResult"/> so the backend can reject duplicate or stale reports without taking
/// a per-row lease.
/// </param>
public sealed record LargePayloadTombstone(
    int PartitionId, long InstanceKey, long PayloadId, string Token, long Revision);
