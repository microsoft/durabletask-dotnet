// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Compression;
using System.Net;
using System.Text;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Microsoft.DurableTask;

/// <summary>
/// Azure Blob Storage implementation of <see cref="PayloadStore"/>.
/// Stores payloads as blobs and returns self-describing opaque tokens in the form
/// "blob:v2:&lt;fullBlobUrl&gt;", where the URL is the blob's absolute URI including the storage account.
/// Legacy "blob:v1:&lt;container&gt;:&lt;blobName&gt;" tokens are still recognized for read back-compatibility.
/// </summary>
public sealed class BlobPayloadStore : PayloadStore
{
    const string TokenPrefixV1 = "blob:v1:";
    const string TokenPrefixV2 = "blob:v2:";
    const string ContentEncodingGzip = "gzip";
    const int MaxRetryAttempts = 8;
    const int BaseDelayMs = 250;
    const int MaxDelayMs = 10_000;
    const int NetworkTimeoutMinutes = 2;
    readonly BlobContainerClient containerClient;
    readonly LargePayloadStorageOptions options;
    readonly BlobClientOptions clientOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="BlobPayloadStore"/> class.
    /// </summary>
    /// <param name="options">The options for the blob payload store.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when neither connection string nor account URI/credential are provided.</exception>
    public BlobPayloadStore(LargePayloadStorageOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        Check.NotNullOrEmpty(options.ContainerName, nameof(options.ContainerName));

        // Validate that either connection string or account URI/credential are provided
        bool hasConnectionString = !string.IsNullOrEmpty(options.ConnectionString);
        bool hasIdentityAuth = options.AccountUri != null && options.Credential != null;

        if (!hasConnectionString && !hasIdentityAuth)
        {
            throw new ArgumentException(
                "Either ConnectionString or AccountUri and Credential must be provided.",
                nameof(options));
        }

        this.clientOptions = new BlobClientOptions
        {
            Retry =
            {
                Mode = RetryMode.Exponential,
                MaxRetries = MaxRetryAttempts,
                Delay = TimeSpan.FromMilliseconds(BaseDelayMs),
                MaxDelay = TimeSpan.FromMilliseconds(MaxDelayMs),
                NetworkTimeout = TimeSpan.FromMinutes(NetworkTimeoutMinutes),
            },
        };

        BlobServiceClient serviceClient = hasIdentityAuth
            ? new BlobServiceClient(options.AccountUri, options.Credential, this.clientOptions)
            : new BlobServiceClient(options.ConnectionString, this.clientOptions);

        this.containerClient = serviceClient.GetBlobContainerClient(options.ContainerName);
    }

    /// <inheritdoc/>
    public override async Task<string> UploadAsync(string payLoad, CancellationToken cancellationToken)
    {
        // One blob per payload using GUID-based name for uniqueness (stable across retries)
        string blobName = $"{Guid.NewGuid():N}";
        BlobClient blob = this.containerClient.GetBlobClient(blobName);

        byte[] payloadBuffer = Encoding.UTF8.GetBytes(payLoad);

        // Ensure container exists (idempotent)
        await this.containerClient.CreateIfNotExistsAsync(PublicAccessType.None, default, default, cancellationToken);

        if (this.options.CompressionEnabled)
        {
            BlobOpenWriteOptions writeOptions = new()
            {
                HttpHeaders = new BlobHttpHeaders { ContentEncoding = ContentEncodingGzip },
            };
            using Stream blobStream = await blob.OpenWriteAsync(true, writeOptions, cancellationToken);
            using GZipStream compressedBlobStream = new(blobStream, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true);

            // using MemoryStream payloadStream = new(payloadBuffer, writable: false);

            // await payloadStream.CopyToAsync(compressedBlobStream, bufferSize: DefaultCopyBufferSize, cancellationToken);
            await WritePayloadAsync(payloadBuffer, compressedBlobStream, cancellationToken);
            await compressedBlobStream.FlushAsync(cancellationToken);
            await blobStream.FlushAsync(cancellationToken);
        }
        else
        {
            using Stream blobStream = await blob.OpenWriteAsync(true, default, cancellationToken);

            // using MemoryStream payloadStream = new(payloadBuffer, writable: false);
            // await payloadStream.CopyToAsync(blobStream, bufferSize: DefaultCopyBufferSize, cancellationToken);
            await WritePayloadAsync(payloadBuffer, blobStream, cancellationToken);
            await blobStream.FlushAsync(cancellationToken);
        }

        return EncodeToken(blob.Uri);
    }

    /// <inheritdoc/>
    public override async Task<string> DownloadAsync(string token, CancellationToken cancellationToken)
    {
        (bool isV2, string container, string name, Uri? blobUri, Uri? containerUri) = DecodeToken(token);

        if (!isV2)
        {
            // v1 tokens do not carry the account, so the payload is assumed to live in the configured container.
            if (!string.Equals(container, this.containerClient.Name, StringComparison.Ordinal))
            {
                throw new ArgumentException("Token container does not match configured container.", nameof(token));
            }

            return await DownloadFromBlobAsync(this.containerClient.GetBlobClient(name), cancellationToken);
        }

        // v2 tokens are self-describing: honor the account and container encoded in the token.
        BlobClient blob;
        if (this.IsConfiguredContainer(containerUri!))
        {
            // Same account and container as the configured store: reuse it (works with any auth mode).
            blob = this.containerClient.GetBlobClient(name);
        }
        else if (this.options.Credential != null)
        {
            // The payload lives in a different account (e.g. the store was repointed). Identity auth can still
            // read it as long as the credential has RBAC access to that account.
            blob = new BlobClient(blobUri, this.options.Credential, this.clientOptions);
        }
        else
        {
            throw new PayloadStorageException(
                $"The externalized payload lives in a different storage account ('{containerUri}') than the " +
                $"currently-configured payload store ('{this.containerClient.Uri}'). Cross-account payload reads " +
                "require identity (AAD) authentication with access to both accounts; connection-string / " +
                "account-key credentials are account-specific and cannot read another account.");
        }

        return await DownloadFromBlobAsync(blob, cancellationToken);
    }

    /// <inheritdoc/>
    public override bool IsKnownPayloadToken(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        return value.StartsWith(TokenPrefixV1, StringComparison.Ordinal)
            || value.StartsWith(TokenPrefixV2, StringComparison.Ordinal);
    }

    /// <summary>
    /// Encodes a self-describing v2 payload token from the blob's absolute URI. The token carries the full blob
    /// URL (including the storage account) so readers can locate the payload without relying on the currently
    /// configured store. <c>BlobClient.Uri</c> contains no SAS or account key and is safe to persist.
    /// </summary>
    /// <param name="blobUri">The absolute URI of the blob holding the payload.</param>
    /// <returns>An opaque payload token in the form "blob:v2:&lt;fullBlobUrl&gt;".</returns>
    internal static string EncodeToken(Uri blobUri) => $"{TokenPrefixV2}{blobUri}";

    /// <summary>
    /// Decodes a payload token. Supports self-describing v2 tokens ("blob:v2:&lt;fullBlobUrl&gt;") and legacy v1
    /// tokens ("blob:v1:&lt;container&gt;:&lt;blobName&gt;"), the latter for read back-compatibility.
    /// </summary>
    /// <param name="token">The payload token to decode.</param>
    /// <returns>
    /// A tuple describing the token: whether it is v2, the container and blob names, and (for v2 only) the
    /// absolute blob URI and its container-level URI.
    /// </returns>
    internal static (bool IsV2, string Container, string Name, Uri? BlobUri, Uri? ContainerUri) DecodeToken(
        string token)
    {
        if (token.StartsWith(TokenPrefixV2, StringComparison.Ordinal))
        {
            string rest = token.Substring(TokenPrefixV2.Length);
            if (!Uri.TryCreate(rest, UriKind.Absolute, out Uri? blobUri))
            {
                throw new ArgumentException("Invalid external payload token format.", nameof(token));
            }

            BlobUriBuilder builder = new(blobUri);
            string container = builder.BlobContainerName;
            string name = builder.BlobName;
            builder.BlobName = string.Empty;
            Uri containerUri = builder.ToUri();
            return (true, container, name, blobUri, containerUri);
        }

        if (token.StartsWith(TokenPrefixV1, StringComparison.Ordinal))
        {
            string rest = token.Substring(TokenPrefixV1.Length);
            int sep = rest.IndexOf(':');
            if (sep <= 0 || sep >= rest.Length - 1)
            {
                throw new ArgumentException("Invalid external payload token format.", nameof(token));
            }

            return (false, rest.Substring(0, sep), rest.Substring(sep + 1), null, null);
        }

        throw new ArgumentException("Invalid external payload token.", nameof(token));
    }

    static async Task WritePayloadAsync(byte[] payloadBuffer, Stream target, CancellationToken cancellationToken)
    {
#if NETSTANDARD2_0
        await target.WriteAsync(payloadBuffer, 0, payloadBuffer.Length, cancellationToken).ConfigureAwait(false);
#else
        await target.WriteAsync(payloadBuffer.AsMemory(0, payloadBuffer.Length), cancellationToken).ConfigureAwait(false);
#endif
    }

    static async Task<string> ReadToEndAsync(StreamReader reader, CancellationToken cancellationToken)
    {
#if NETSTANDARD2_0
        cancellationToken.ThrowIfCancellationRequested();
        return await reader.ReadToEndAsync().ConfigureAwait(false);
#elif NET8_0_OR_GREATER
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
#else
        return await reader.ReadToEndAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
#endif
    }

    static async Task<string> DownloadFromBlobAsync(BlobClient blob, CancellationToken cancellationToken)
    {
        try
        {
            using BlobDownloadStreamingResult result = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
            Stream contentStream = result.Content;
            bool isGzip = string.Equals(
                result.Details.ContentEncoding, ContentEncodingGzip, StringComparison.OrdinalIgnoreCase);

            if (isGzip)
            {
                using GZipStream decompressed = new(contentStream, CompressionMode.Decompress);
                using StreamReader reader = new(decompressed, Encoding.UTF8);
                return await ReadToEndAsync(reader, cancellationToken);
            }

            using StreamReader uncompressedReader = new(contentStream, Encoding.UTF8);
            return await ReadToEndAsync(uncompressedReader, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            throw new PayloadStorageException(
                $"The blob '{blob.Name}' was not found in container '{blob.BlobContainerName}'. " +
                "The payload may have been deleted or the container was never created.",
                ex);
        }
    }

    bool IsConfiguredContainer(Uri tokenContainerUri)
    {
        Uri configured = this.containerClient.Uri;
        return string.Equals(tokenContainerUri.Scheme, configured.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(tokenContainerUri.Host, configured.Host, StringComparison.OrdinalIgnoreCase)
            && tokenContainerUri.Port == configured.Port
            && string.Equals(
                tokenContainerUri.AbsolutePath.TrimEnd('/'),
                configured.AbsolutePath.TrimEnd('/'),
                StringComparison.Ordinal);
    }
}
