// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Abstraction for storing and retrieving large payloads out-of-band.
/// </summary>
public interface IPayloadStore
{
    /// <summary>
    /// Uploads a payload and returns an opaque reference token that can be embedded in orchestration messages.
    /// </summary>
    /// <param name="payloadBytes">The payload bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Opaque reference token.</returns>
    Task<string> UploadAsync(ReadOnlyMemory<byte> payloadBytes, CancellationToken cancellationToken);

    /// <summary>
    /// Downloads the payload referenced by the token.
    /// </summary>
    /// <param name="token">The opaque reference token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Payload string.</returns>
    Task<string> DownloadAsync(string token, CancellationToken cancellationToken);

    /// <summary>
    /// Returns true if the specified value appears to be a token understood by this store.
    /// Implementations should not throw for unknown tokens.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns><c>true</c> if the value is a token issued by this store; otherwise, <c>false</c>.</returns>
    bool IsKnownPayloadToken(string value);
}