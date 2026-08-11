// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// The outcome of deleting a payload through <see cref="PayloadStore.DeleteAsync"/>. All three values are
/// successful terminal outcomes; failures are surfaced as exceptions instead.
/// </summary>
public enum PayloadDeleteOutcome
{
    /// <summary>
    /// The payload's backing object was deleted by this call.
    /// </summary>
    Deleted,

    /// <summary>
    /// The payload's backing object was already absent. Deletion is idempotent, so this is a success.
    /// </summary>
    AlreadyAbsent,

    /// <summary>
    /// The backing object exists but does not carry the store's ownership marker, so the store did not
    /// create it and left it untouched. The payload reference is still resolved, because an object the
    /// store never wrote is not the store's to delete.
    /// </summary>
    NotStoreOwned,
}
