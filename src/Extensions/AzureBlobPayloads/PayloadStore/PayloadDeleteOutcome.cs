// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// The outcome of deleting a payload through <see cref="PayloadStore.DeleteAsync"/>.
/// <see cref="Deleted"/>, <see cref="AlreadyAbsent"/>, and <see cref="NotStoreOwned"/> are successful terminal
/// outcomes. <see cref="Unspecified"/> does not confirm success; failures are surfaced as exceptions.
/// </summary>
public enum PayloadDeleteOutcome
{
    /// <summary>
    /// No confirmed outcome was supplied. This default value is not a terminal success; the payload reference
    /// must be preserved for retry.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// The delete was accepted by storage on this call - the backing object's current version was removed.
    /// This means the delete was accepted, not that the underlying bytes were reclaimed: if the storage account
    /// has blob versioning or blob soft delete enabled, the prior version or the soft-deleted blob is retained
    /// until a lifecycle-management policy or the configured retention period removes it.
    /// </summary>
    Deleted = 1,

    /// <summary>
    /// The payload's backing object was already absent. Deletion is idempotent, so this is a success.
    /// </summary>
    AlreadyAbsent = 2,

    /// <summary>
    /// The backing object exists but does not carry the store's ownership marker, so the store did not
    /// create it and left it untouched. The payload reference is still resolved, because an object the
    /// store never wrote is not the store's to delete.
    /// </summary>
    NotStoreOwned = 3,
}
