// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Serializable acknowledgement that the worker has deleted the blob for a tombstoned payload, so the
/// backend can hard-delete the soft-deleted row. Mirrors the <c>PayloadPurgeAck</c> protobuf message.
/// </summary>
/// <param name="PartitionId">The backend partition that owns the payload row.</param>
/// <param name="InstanceKey">The orchestration instance key the payload belongs to.</param>
/// <param name="PayloadId">The backend identifier of the soft-deleted payload row.</param>
public sealed record PayloadPurgeAckDto(int PartitionId, long InstanceKey, long PayloadId);
