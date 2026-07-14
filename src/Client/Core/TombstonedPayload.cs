// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Serializable representation of a tombstoned payload the backend has soft-deleted and whose blob the
/// worker should delete. Mirrors the <c>TombstonedPayload</c> protobuf message but is safe to pass through
/// the orchestration/activity boundary.
/// </summary>
/// <param name="PartitionId">The backend partition that owns the payload row.</param>
/// <param name="InstanceKey">The orchestration instance key the payload belongs to.</param>
/// <param name="PayloadId">The backend identifier of the soft-deleted payload row.</param>
/// <param name="Token">The externalized payload token whose backing blob should be deleted.</param>
public sealed record TombstonedPayload(int PartitionId, long InstanceKey, long PayloadId, string Token);
