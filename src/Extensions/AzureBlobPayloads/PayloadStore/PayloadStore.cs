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
    /// Returns true if the specified value appears to be a token understood by this store.
    /// Implementations should not throw for unknown tokens.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns><c>true</c> if the value is a token issued by this store; otherwise, <c>false</c>.</returns>
    public abstract bool IsKnownPayloadToken(string value);
}
