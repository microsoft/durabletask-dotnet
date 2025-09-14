// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;
using System.IO.Compression;
using System.Text;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Microsoft.DurableTask.Converters;

/// <summary>
/// Azure Blob Storage implementation of <see cref="IPayloadStore"/>.
/// Stores payloads as blobs and returns opaque tokens in the form "blob:v1:&lt;container&gt;:&lt;blobName&gt;".
/// </summary>
public sealed class BlobPayloadStore : IPayloadStore
{
    const string TokenPrefix = "blob:v1:";
    const string ContentEncodingGzip = "gzip";
    const int DefaultCopyBufferSize = 81920;
    const int MaxRetryAttempts = 8;
    const int BaseDelayMs = 250;
    const int MaxDelayMs = 10_000;
    const int MaxJitterMs = 100;

    // Jitter RNG for retry backoff
    static readonly object RandomLock = new object();
    static readonly Random SharedRandom = new Random();
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

        BlobClientOptions clientOptions = new()
        {
            Retry =
            {
                Mode = RetryMode.Exponential,
                MaxRetries = MaxRetryAttempts,
                Delay = TimeSpan.FromMilliseconds(BaseDelayMs),
                MaxDelay = TimeSpan.FromSeconds(10),
                NetworkTimeout = TimeSpan.FromMinutes(2),
            },
        };
        BlobServiceClient serviceClient = new(options.ConnectionString, clientOptions);
        this.containerClient = serviceClient.GetBlobContainerClient(options.ContainerName);
    }

    /// <inheritdoc/>
    public async Task<string> UploadAsync(ReadOnlyMemory<byte> payloadBytes, CancellationToken cancellationToken)
    {
        // One blob per payload using GUID-based name for uniqueness (stable across retries)
        string timestamp = DateTimeOffset.UtcNow.ToString("yyyy/MM/dd/HH/mm/ss", CultureInfo.InvariantCulture);
        string blobName = $"{timestamp}/{Guid.NewGuid():N}";
        BlobClient blob = this.containerClient.GetBlobClient(blobName);

        byte[] payloadBuffer = payloadBytes.ToArray();

        string token = await WithTransientRetryAsync(
        async ct =>
        {
            // Ensure container exists (idempotent)
            await this.containerClient.CreateIfNotExistsAsync(PublicAccessType.None, default, default, ct);

            if (this.options.CompressPayloads)
            {
                BlobOpenWriteOptions writeOptions = new()
                {
                    HttpHeaders = new BlobHttpHeaders { ContentEncoding = ContentEncodingGzip },
                };
                using Stream blobStream = await blob.OpenWriteAsync(true, writeOptions, ct);
                using GZipStream compressedBlobStream = new(blobStream, CompressionLevel.Optimal, leaveOpen: true);
                using MemoryStream payloadStream = new(payloadBuffer, writable: false);

                await payloadStream.CopyToAsync(compressedBlobStream, bufferSize: DefaultCopyBufferSize, ct);
                await compressedBlobStream.FlushAsync(ct);
                await blobStream.FlushAsync(ct);
            }
            else
            {
                using Stream blobStream = await blob.OpenWriteAsync(true, default, ct);
                using MemoryStream payloadStream = new(payloadBuffer, writable: false);
                await payloadStream.CopyToAsync(blobStream, bufferSize: DefaultCopyBufferSize, ct);
                await blobStream.FlushAsync(ct);
            }

            return EncodeToken(this.containerClient.Name, blobName);
        },
        cancellationToken);

        return token;
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

        return await WithTransientRetryAsync(
        async ct =>
        {
            using BlobDownloadStreamingResult result = await blob.DownloadStreamingAsync(cancellationToken: ct);
            Stream contentStream = result.Content;
            bool isGzip = string.Equals(
                result.Details.ContentEncoding, ContentEncodingGzip, StringComparison.OrdinalIgnoreCase);

            if (isGzip)
            {
                using GZipStream decompressed = new(contentStream, CompressionMode.Decompress);
                using StreamReader reader = new(decompressed, Encoding.UTF8);
                return await reader.ReadToEndAsync();
            }

            using StreamReader uncompressedReader = new(contentStream, Encoding.UTF8);
            return await uncompressedReader.ReadToEndAsync();
        },
        cancellationToken);
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

    static async Task<T> WithTransientRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        const int maxAttempts = MaxRetryAttempts;
        TimeSpan baseDelay = TimeSpan.FromMilliseconds(BaseDelayMs);
        int attempt = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await operation(cancellationToken);
            }
            catch (RequestFailedException ex) when (IsTransient(ex) && attempt < maxAttempts - 1)
            {
                attempt++;
                TimeSpan delay = ComputeBackoff(baseDelay, attempt);
                await Task.Delay(delay, cancellationToken);
            }
            catch (IOException) when (attempt < maxAttempts - 1)
            {
                attempt++;
                TimeSpan delay = ComputeBackoff(baseDelay, attempt);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    static bool IsTransient(RequestFailedException ex)
    {
        return ex.Status == 503 || ex.Status == 502 || ex.Status == 500 || ex.Status == 429 ||
            string.Equals(ex.ErrorCode, "ServerBusy", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ex.ErrorCode, "OperationTimedOut", StringComparison.OrdinalIgnoreCase);
    }

    static TimeSpan ComputeBackoff(TimeSpan baseDelay, int attempt)
    {
        double factor = Math.Pow(2, Math.Min(attempt, 6));
        int jitterMs;
        lock (RandomLock)
        {
            jitterMs = SharedRandom.Next(0, MaxJitterMs);
        }

        return TimeSpan.FromMilliseconds(Math.Min((baseDelay.TotalMilliseconds * factor) + jitterMs, MaxDelayMs));
    }
}
