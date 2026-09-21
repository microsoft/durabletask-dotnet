// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Serializable representation of a tombstoned large-payload row whose external blob a credentialed caller
/// must delete. Mirrors the <c>LargePayloadTombstone</c> protobuf message but is safe to pass through the
/// orchestration/activity boundary.
/// </summary>
/// <param name="TombstoneToken">
/// The opaque, backend-issued correlation token for this exact tombstone version. Its format is deliberately
/// not part of the contract: callers must not parse it, and must echo it back unchanged in the corresponding
/// <see cref="LargePayloadPurgeResult"/> so the backend can resolve the row it came from.
/// </param>
/// <param name="PayloadToken">
/// The self-describing <c>blob:v2:{fullBlobUrl}</c> payload token whose backing blob should be deleted.
/// </param>
public sealed record LargePayloadTombstone(string TombstoneToken, string PayloadToken)
{
    /// <summary>
    /// The maximum number of tombstones a single <c>GetLargePayloadTombstones</c> request may ask for. The
    /// service clamps a larger request down to its own maximum, so this is the largest value that is worth
    /// asking for rather than a value that will be rejected. Validators that bound a caller-supplied limit
    /// compare against this shared value rather than a hard-coded literal so the bound cannot drift between
    /// the client and the auto-purge extension.
    /// </summary>
    public const int MaxRequestLimit = 1000;
}
