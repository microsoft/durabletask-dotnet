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
/// Mirrors the <c>LargePayloadPurgeReason</c> protobuf enum.
/// </summary>
public enum LargePayloadPurgeReason
{
    /// <summary>
    /// No reason was specified.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// The blob was deleted by this attempt, reclaiming its bytes. Reported with
    /// <see cref="LargePayloadPurgeDisposition.Deleted"/>.
    /// </summary>
    BlobDeleted = 1,

    /// <summary>
    /// The blob was already absent, so deletion was a no-op and no bytes were reclaimed. Deletion is idempotent,
    /// so this is a success rather than a failure; it is reported separately from <see cref="BlobDeleted"/>
    /// because a high rate of it indicates duplicate tombstones. Reported with
    /// <see cref="LargePayloadPurgeDisposition.Deleted"/>.
    /// </summary>
    BlobAlreadyAbsent = 2,

    /// <summary>
    /// The blob did not carry the payload store's ownership marker, so it was left untouched. This is an
    /// expected outcome, not a defect: the token text merely matched the v2 grammar, and the payload column is
    /// customer-writable. The tombstone is still resolved, because a blob the store does not own will never
    /// become deletable. Reported separately from <see cref="BlobDeleted"/> so that "resolved without
    /// reclaiming bytes" stays countable. Reported with <see cref="LargePayloadPurgeDisposition.Deleted"/>.
    /// </summary>
    BlobNotStoreOwned = 3,

    /// <summary>
    /// The deletion failed against storage: network failure, timeout, outage, throttling, an unreachable
    /// account, or an authorization failure. All of these are reconfigurable or self-healing, and they are not
    /// subdivided because <see cref="LargePayloadPurgeResult.StorageErrorCode"/> already carries the specific
    /// status. Reported with <see cref="LargePayloadPurgeDisposition.Retry"/>.
    /// </summary>
    StorageFailure = 10,

    /// <summary>
    /// The registered payload store does not implement deletion. Every payload fails the same way, so this is a
    /// deployment-wide condition rather than a per-row one, and it stays recoverable until an operator registers
    /// a store that can delete. <see cref="LargePayloadPurgeResult.StorageErrorCode"/> is empty because storage
    /// was never contacted, which is why this is not folded into <see cref="StorageFailure"/>. Reported with
    /// <see cref="LargePayloadPurgeDisposition.Retry"/>.
    /// </summary>
    StoreCannotDelete = 11,

    /// <summary>
    /// The token cannot be acted on as it stands: its body does not parse, it names a version this worker does
    /// not support, or storage rejected it as permanently invalid.
    /// <see cref="LargePayloadPurgeResult.StorageErrorCode"/> distinguishes the storage-rejected case, where it
    /// is populated, from the parse cases, where it is empty. Reported with
    /// <see cref="LargePayloadPurgeDisposition.Quarantined"/>, except for an unsupported version prefix, which
    /// is reported with <see cref="LargePayloadPurgeDisposition.Retry"/> because an SDK upgrade resolves it.
    /// </summary>
    TokenNotPurgeable = 20,
}
