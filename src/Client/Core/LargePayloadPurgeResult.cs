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
/// than as a lossy classification.
/// </remarks>
/// <param name="TombstoneToken">
/// The opaque correlation token echoed unchanged from the fetched <see cref="LargePayloadTombstone"/>. It is
/// what identifies the row being reported on, so it must be passed back exactly as received: callers must not
/// parse it, derive from it, or construct one.
/// <para>
/// Opaqueness here is encapsulation, not security. The token is not an authentication credential and carries
/// no integrity guarantee, so treating a well-formed token as proof that the caller was entitled to report on
/// that row would be wrong. Authentication and task-hub scope are the security boundary.
/// </para>
/// </param>
/// <param name="Disposition">The disposition of the deletion attempt.</param>
public sealed record LargePayloadPurgeResult(
    string TombstoneToken,
    LargePayloadPurgeDisposition Disposition);
