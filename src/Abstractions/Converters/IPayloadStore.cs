// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Converters;

/// <summary>
/// Abstraction for storing and retrieving large payloads out-of-band.
/// </summary>
public interface IPayloadStore
{
    /// <summary>
    /// Uploads a payload and returns an opaque reference token that can be embedded in orchestration messages.
    /// </summary>
    /// <param name="contentType">The content type of the payload (e.g., application/json).</param>
    /// <param name="payloadBytes">The payload bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Opaque reference token.</returns>
    Task<string> UploadAsync(string contentType, ReadOnlyMemory<byte> payloadBytes, CancellationToken cancellationToken);

    /// <summary>
    /// Downloads the payload referenced by the token.
    /// </summary>
    /// <param name="token">The opaque reference token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Payload string.</returns>
    Task<string> DownloadAsync(string token, CancellationToken cancellationToken);
}
