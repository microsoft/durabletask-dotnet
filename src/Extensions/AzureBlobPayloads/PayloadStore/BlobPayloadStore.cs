// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;
using System.IO.Compression;
using System.Text;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.DurableTask.Converters;

namespace Microsoft.DurableTask;

/// <summary>
/// Azure Blob Storage implementation of <see cref="IPayloadStore"/>.
/// Stores payloads as blobs and returns opaque tokens in the form "blob:v1:&lt;container&gt;:&lt;blobName&gt;".
/// </summary>
public sealed class BlobPayloadStore : IPayloadStore
{
    const string TokenPrefix = "blob:v1:";
    readonly BlobContainerClient containerClient;
    readonly LargePayloadStorageOptions options;

    /// <summary>
    /// Initializes a new instance of the <see cref="BlobPayloadStore"/> class.
    /// </summary>
    /// <param name="options">The options for the blob payload store.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="options.ConnectionString"/> is null or empty.</exception>
    public BlobPayloadStore(LargePayloadStorageOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));

        Check.NotNullOrEmpty(options.ConnectionString, nameof(options.ConnectionString));
        Check.NotNullOrEmpty(options.ContainerName, nameof(options.ContainerName));

        BlobServiceClient serviceClient = new(options.ConnectionString);
        this.containerClient = serviceClient.GetBlobContainerClient(options.ContainerName);
    }

    /// <inheritdoc/>
    public async Task<string> UploadAsync(ReadOnlyMemory<byte> payloadBytes, CancellationToken cancellationToken)
    {
        // Ensure container exists
        await this.containerClient.CreateIfNotExistsAsync(PublicAccessType.None, default, default, cancellationToken);

        // One blob per payload using GUID-based name for uniqueness
        string timestamp = DateTimeOffset.UtcNow.ToString("yyyy/MM/dd/HH/mm/ss", CultureInfo.InvariantCulture);
        string blobName = $"{timestamp}/{Guid.NewGuid():N}";
        BlobClient blob = this.containerClient.GetBlobClient(blobName);

        byte[] payloadBuffer = payloadBytes.ToArray();

        // Upload streaming, optionally compressing and marking ContentEncoding
        if (this.options.CompressPayloads)
        {
            BlobOpenWriteOptions writeOptions = new()
            {
                HttpHeaders = new BlobHttpHeaders { ContentEncoding = "gzip" },
            };
            using Stream blobStream = await blob.OpenWriteAsync(true, writeOptions, cancellationToken);
            using GZipStream compressedBlobStream = new(blobStream, CompressionLevel.Optimal, leaveOpen: true);
            using MemoryStream payloadStream = new(payloadBuffer, writable: false);

            await payloadStream.CopyToAsync(compressedBlobStream, bufferSize: 81920, cancellationToken);
            await compressedBlobStream.FlushAsync(cancellationToken);
            await blobStream.FlushAsync(cancellationToken);
        }
        else
        {
            using Stream blobStream = await blob.OpenWriteAsync(true, default, cancellationToken);
            using MemoryStream payloadStream = new(payloadBuffer, writable: false);
            await payloadStream.CopyToAsync(blobStream, bufferSize: 81920, cancellationToken);
            await blobStream.FlushAsync(cancellationToken);
        }

        return EncodeToken(this.containerClient.Name, blobName);
    }

    /// <inheritdoc/>
    public async Task<string> DownloadAsync(string token, CancellationToken cancellationToken)
    {
        (string container, string name) = DecodeToken(token);
        if (!string.Equals(container, this.containerClient.Name, StringComparison.Ordinal))
        {
            throw new ArgumentException("Token container does not match configured container.", nameof(token));
        }

        BlobClient blob = this.containerClient.GetBlobClient(name);
        using BlobDownloadStreamingResult result = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
        Stream contentStream = result.Content;
        bool isGzip = string.Equals(
            result.Details.ContentEncoding, "gzip", StringComparison.OrdinalIgnoreCase);

        if (isGzip)
        {
            using GZipStream decompressed = new(contentStream, CompressionMode.Decompress);
            using StreamReader reader = new(decompressed, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }

        using StreamReader uncompressedReader = new(contentStream, Encoding.UTF8);
        return await uncompressedReader.ReadToEndAsync();
    }

    /// <inheritdoc/>
    public bool IsKnownPayloadToken(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        return value.StartsWith(TokenPrefix, StringComparison.Ordinal);
    }

    static string EncodeToken(string container, string name) => $"blob:v1:{container}:{name}";

    static (string Container, string Name) DecodeToken(string token)
    {
        if (!token.StartsWith("blob:v1:", StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid external payload token.", nameof(token));
        }

        string rest = token.Substring("blob:v1:".Length);
        int sep = rest.IndexOf(':');
        if (sep <= 0 || sep >= rest.Length - 1)
        {
            throw new ArgumentException("Invalid external payload token format.", nameof(token));
        }

        return (rest.Substring(0, sep), rest.Substring(sep + 1));
    }
}
