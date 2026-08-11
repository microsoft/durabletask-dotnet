// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Abstraction for storing and retrieving large payloads out-of-band.
/// </summary>
public abstract class PayloadStore
{
    /// <summary>
    /// Uploads a payload and returns an opaque reference token that can be embedded in orchestration messages.
    /// </summary>
    /// <param name="payLoad">The payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Opaque reference token.</returns>
    public abstract Task<string> UploadAsync(string payLoad, CancellationToken cancellationToken);

    /// <summary>
    /// Downloads the payload referenced by the token.
    /// </summary>
    /// <param name="token">The opaque reference token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Payload string.</returns>
    public abstract Task<string> DownloadAsync(string token, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the payload referenced by the token. Implementations that support deletion must be
    /// idempotent: deleting a payload that no longer exists is a no-op and must not throw.
    /// </summary>
    /// <remarks>
    /// The default implementation throws <see cref="NotSupportedException"/>. Stores that externalize
    /// payloads to deletable storage (for example Azure Blob Storage) should override it. It is declared
    /// virtual rather than abstract so that adding it does not break existing external subclasses.
    /// Implementations must delete only objects they created; an object that carries no proof of the
    /// store's ownership must be left untouched and reported as
    /// <see cref="PayloadDeleteOutcome.NotStoreOwned"/>.
    /// </remarks>
    /// <param name="token">The opaque reference token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The outcome of the deletion: whether the object was deleted, was already absent, or was left in
    /// place because the store does not own it.
    /// </returns>
    public virtual Task<PayloadDeleteOutcome> DeleteAsync(string token, CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            $"This {nameof(PayloadStore)} implementation does not support deleting payloads.");

    /// <summary>
    /// Returns true if the specified value appears to be a token understood by this store.
    /// Implementations should not throw for unknown tokens.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns><c>true</c> if the value is a token issued by this store; otherwise, <c>false</c>.</returns>
    public abstract bool IsKnownPayloadToken(string value);
}
