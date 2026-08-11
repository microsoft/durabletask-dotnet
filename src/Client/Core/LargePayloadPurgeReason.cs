// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

/// <summary>
/// A stable, bounded reason for a <see cref="LargePayloadPurgeDisposition"/>. Never carries a token or raw
/// exception text, because tokens expose the storage account, container, and blob path. Mirrors the
/// <c>LargePayloadPurgeReason</c> protobuf enum.
/// </summary>
public enum LargePayloadPurgeReason
{
    /// <summary>
    /// No reason was specified.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// The blob was deleted by this attempt. Reported with <see cref="LargePayloadPurgeDisposition.Deleted"/>.
    /// </summary>
    BlobDeleted = 1,

    /// <summary>
    /// The blob was already absent. Deletion is idempotent, so this is a success. Reported with
    /// <see cref="LargePayloadPurgeDisposition.Deleted"/>.
    /// </summary>
    BlobAlreadyAbsent = 2,

    /// <summary>
    /// The blob did not carry the payload store's ownership marker, so it was left untouched. This is an
    /// expected outcome, not a defect: the token text merely matched the v2 grammar. The tombstone is still
    /// resolved because the blob is not the store's to delete. Reported with
    /// <see cref="LargePayloadPurgeDisposition.Deleted"/>.
    /// </summary>
    BlobNotStoreOwned = 3,

    /// <summary>
    /// Network failure, timeout, storage outage, throttling, or a 5xx response. Reported with
    /// <see cref="LargePayloadPurgeDisposition.Retry"/>.
    /// </summary>
    TransientStorageFailure = 10,

    /// <summary>
    /// The registered payload store does not implement deletion. Every payload would fail the same way, so the
    /// work is kept recoverable until an operator registers a store that can delete. Reported with
    /// <see cref="LargePayloadPurgeDisposition.Retry"/>.
    /// </summary>
    StoreCannotDelete = 11,

    /// <summary>
    /// The token is well formed but points at a storage account this worker's credential cannot reach.
    /// Recoverable after a configuration or credential change. Reported with
    /// <see cref="LargePayloadPurgeDisposition.Retry"/>.
    /// </summary>
    StorageAccountUnreachable = 12,

    /// <summary>
    /// The token uses a recognized-but-newer version prefix this worker does not understand. Recoverable after
    /// an SDK upgrade, so it earns a long defer rather than quarantine. Reported with
    /// <see cref="LargePayloadPurgeDisposition.Retry"/>.
    /// </summary>
    UnsupportedTokenVersion = 13,

    /// <summary>
    /// Authorization failed in a way that may be transient or reconfigurable (401/403). Reported with
    /// <see cref="LargePayloadPurgeDisposition.Retry"/>.
    /// </summary>
    StorageAuthorizationFailed = 14,

    /// <summary>
    /// The token carries a known version prefix but its body does not parse. Because the SDK and backend
    /// control both sides of the protocol, this indicates a producer, corruption, or compatibility bug.
    /// Reported with <see cref="LargePayloadPurgeDisposition.Quarantined"/>.
    /// </summary>
    MalformedToken = 20,

    /// <summary>
    /// Storage rejected a request generated from a well-formed token as permanently invalid (HTTP 400, for
    /// example InvalidUri / InvalidResourceName). Retrying can never succeed. Reported with
    /// <see cref="LargePayloadPurgeDisposition.Quarantined"/>.
    /// </summary>
    InvalidStorageRequest = 21,

    /// <summary>
    /// A legacy v1 token reached the worker. This is an invariant violation, because the backend excludes v1
    /// at insertion time. It cannot be safely deleted (no storage account in the token) and cannot be fixed by
    /// retrying, so the evidence is preserved instead. Reported with
    /// <see cref="LargePayloadPurgeDisposition.Quarantined"/>.
    /// </summary>
    LegacyV1Token = 22,
}
