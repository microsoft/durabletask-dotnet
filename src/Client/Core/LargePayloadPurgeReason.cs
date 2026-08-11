// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Why a row received its <see cref="LargePayloadPurgeDisposition"/>. Diagnostic only: the backend acts on the
/// disposition alone and never branches on this value, so it exists to make a stuck or non-reclaiming ledger
/// explainable without access to worker logs. Deliberately coarse - granularity matches the number of distinct
/// operator responses, not the number of distinct causes, because
/// <see cref="LargePayloadPurgeResult.StorageErrorCode"/> already carries the specific storage status. Never
/// carries a token or raw exception text, because a token exposes the storage account, container, and blob path.
/// <para>
/// Reason and disposition are orthogonal and there is no fixed mapping between them. A reason says what the
/// worker found; a disposition says whether trying again can change it. Most reasons occur with exactly one
/// disposition, but <see cref="TokenNotPurgeable"/> deliberately occurs with two. Do not assert a
/// reason-to-disposition mapping anywhere.
/// </para>
/// Mirrors the <c>LargePayloadPurgeReason</c> protobuf enum.
/// </summary>
public enum LargePayloadPurgeReason
{
    /// <summary>
    /// No reason was specified.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// Reported with <see cref="LargePayloadPurgeDisposition.Deleted"/>. The blob was deleted by this attempt,
    /// reclaiming its bytes.
    /// </summary>
    BlobDeleted = 1,

    /// <summary>
    /// Reported with <see cref="LargePayloadPurgeDisposition.Deleted"/>. The blob was already absent, so deletion
    /// was a no-op and no bytes were reclaimed by this attempt. Deletion is idempotent, so this is a success
    /// rather than a failure. Reported separately from <see cref="BlobDeleted"/> because a high rate of it
    /// indicates duplicate tombstones.
    /// </summary>
    BlobAlreadyAbsent = 2,

    /// <summary>
    /// Reported with <see cref="LargePayloadPurgeDisposition.Deleted"/>. The blob was left in place because it
    /// did not carry the payload store's ownership marker, meaning the store did not write it. The token text
    /// merely matched the v2 grammar; the payload column is customer-writable, so matching text is not proof of
    /// ownership. This is an expected outcome rather than a defect. The tombstone is still resolved, because a
    /// blob the store does not own will never become deletable and retrying forever would leak the row. Reported
    /// separately so that "resolved without reclaiming bytes" stays countable.
    /// </summary>
    BlobNotStoreOwned = 3,

    /// <summary>
    /// Reported with <see cref="LargePayloadPurgeDisposition.Retry"/>. The deletion failed against storage:
    /// network failure, timeout, outage, throttling, an unreachable account, or an authorization failure. All of
    /// these are reconfigurable or self-healing, and they are not subdivided here because
    /// <see cref="LargePayloadPurgeResult.StorageErrorCode"/> already carries the specific status. Subdividing
    /// would encode the same fact twice.
    /// </summary>
    StorageFailure = 10,

    /// <summary>
    /// Reported with <see cref="LargePayloadPurgeDisposition.Retry"/>. The registered payload store does not
    /// implement deletion. Every payload fails the same way, so this is a deployment-wide condition rather than a
    /// per-row one, and it is kept recoverable until an operator registers a store that can delete.
    /// <see cref="LargePayloadPurgeResult.StorageErrorCode"/> is empty because storage was never contacted, which
    /// is why this cannot be folded into <see cref="StorageFailure"/>.
    /// </summary>
    StoreCannotDelete = 11,

    /// <summary>
    /// The worker could not act on the token. This reason is reported with TWO dispositions, and a consumer must
    /// not assume either one.
    /// <para>
    /// Reported with <see cref="LargePayloadPurgeDisposition.Quarantined"/> when the token can never become
    /// usable: its body does not parse, it is a legacy v1 token, or storage rejected a well-formed token as
    /// permanently invalid. The SDK and backend control both sides of this protocol, so reaching that state
    /// indicates a producer, corruption, or compatibility bug, and the evidence is preserved rather than
    /// discarded.
    /// </para>
    /// <para>
    /// Reported with <see cref="LargePayloadPurgeDisposition.Retry"/> in exactly one case: the token names a
    /// version this worker does not understand. Nothing is wrong with that token, since a newer worker can read
    /// it, so an SDK upgrade resolves it. The asymmetry is deliberate: quarantining it would be permanent and
    /// unrecoverable, whereas a retry that never succeeds only leaves the row idle and visible.
    /// </para>
    /// <see cref="LargePayloadPurgeResult.StorageErrorCode"/> distinguishes the storage-rejected case, where it
    /// is populated, from the parse and unknown-version cases, where it is empty.
    /// </summary>
    TokenNotPurgeable = 20,
}
