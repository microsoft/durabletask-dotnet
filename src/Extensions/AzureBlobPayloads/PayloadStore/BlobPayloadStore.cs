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
/// Stores payloads as blobs and returns opaque tokens in the form "blob:v1:&lt;container&gt;:&lt;blobName&gt;".
/// </summary>
public sealed class BlobPayloadStore : PayloadStore
{
    const string TokenPrefix = "blob:v1:";
    const string ContentEncodingGzip = "gzip";
    const int MaxRetryAttempts = 8;
    const int BaseDelayMs = 250;
    const int MaxDelayMs = 10_000;
    const int NetworkTimeoutMinutes = 2;
    readonly BlobContainerClient containerClient;
    readonly LargePayloadStorageOptions options;

    // Caches the single in-flight (or completed) container-initialization task so that
    // concurrent/subsequent uploads don't each issue their own CreateIfNotExistsAsync request.
    // Null means "not yet attempted" or "needs to be retried"; see EnsureContainerExistsAsync.
    Task? containerInitializationTask;

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

        BlobClientOptions clientOptions = new()
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
            ? new BlobServiceClient(options.AccountUri, options.Credential, clientOptions)
            : new BlobServiceClient(options.ConnectionString, clientOptions);

        this.containerClient = serviceClient.GetBlobContainerClient(options.ContainerName);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BlobPayloadStore"/> class using an existing
    /// container client. Intended for unit testing only.
    /// </summary>
    /// <param name="options">The options for the blob payload store.</param>
    /// <param name="containerClient">The blob container client to use.</param>
    internal BlobPayloadStore(LargePayloadStorageOptions options, BlobContainerClient containerClient)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.containerClient = containerClient ?? throw new ArgumentNullException(nameof(containerClient));
    }

    /// <inheritdoc/>
    public override async Task<string> UploadAsync(string payLoad, CancellationToken cancellationToken)
    {
        // One blob per payload using GUID-based name for uniqueness (stable across retries)
        string blobName = $"{Guid.NewGuid():N}";
        BlobClient blob = this.containerClient.GetBlobClient(blobName);

        byte[] payloadBuffer = Encoding.UTF8.GetBytes(payLoad);

        // Ensure container exists. Cached/single-flight after the first successful call so we
        // don't pay for an extra CreateIfNotExistsAsync request/transaction on every upload.
        await this.EnsureContainerExistsAsync(cancellationToken).ConfigureAwait(false);

        try
        {
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
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == BlobErrorCode.ContainerNotFound)
        {
            // The container existed when we last verified/created it but has since been deleted
            // (e.g. by an operator). Clear the cached initialization state so the next upload
            // attempts to recreate the container, keeping deliberate deletion recoverable.
            Volatile.Write(ref this.containerInitializationTask, null);
            throw;
        }

        return EncodeToken(this.containerClient.Name, blobName);
    }

    /// <inheritdoc/>
    public override async Task<string> DownloadAsync(string token, CancellationToken cancellationToken)
    {
        (string container, string name) = DecodeToken(token);
        if (!string.Equals(container, this.containerClient.Name, StringComparison.Ordinal))
        {
            throw new ArgumentException("Token container does not match configured container.", nameof(token));
        }

        BlobClient blob = this.containerClient.GetBlobClient(name);

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
                $"The blob '{name}' was not found in container '{container}'. " +
                "The payload may have been deleted or the container was never created.",
                ex);
        }
    }

    /// <inheritdoc/>
    public override bool IsKnownPayloadToken(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        return value.StartsWith(TokenPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Awaits a shared initialization task while still honoring the caller's own cancellation
    /// token, without cancelling the shared task itself.
    /// </summary>
    static async Task WaitForInitializationAsync(Task initializationTask, CancellationToken cancellationToken)
    {
#if NETSTANDARD2_0
        if (!cancellationToken.CanBeCanceled || initializationTask.IsCompleted)
        {
            await initializationTask.ConfigureAwait(false);
            return;
        }

        TaskCompletionSource<bool> cancellationTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), cancellationTcs))
        {
            Task completed = await Task.WhenAny(initializationTask, cancellationTcs.Task).ConfigureAwait(false);
            if (completed == cancellationTcs.Task)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        await initializationTask.ConfigureAwait(false);
#else
        await initializationTask.WaitAsync(cancellationToken).ConfigureAwait(false);
#endif
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

    static string EncodeToken(string container, string name) => $"blob:v1:{container}:{name}";

    static (string Container, string Name) DecodeToken(string token)
    {
        if (!token.StartsWith(TokenPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid external payload token.", nameof(token));
        }

        string rest = token.Substring(TokenPrefix.Length);
        int sep = rest.IndexOf(':');
        if (sep <= 0 || sep >= rest.Length - 1)
        {
            throw new ArgumentException("Invalid external payload token format.", nameof(token));
        }

        return (rest.Substring(0, sep), rest.Substring(sep + 1));
    }

    /// <summary>
    /// Ensures the container exists, issuing at most one <c>CreateIfNotExistsAsync</c> request
    /// across all concurrent/subsequent callers. The result is cached for the lifetime of this
    /// instance once it completes successfully; a failed attempt is not cached, so the next
    /// caller retries.
    /// </summary>
    async Task EnsureContainerExistsAsync(CancellationToken cancellationToken)
    {
        Task initializationTask = Volatile.Read(ref this.containerInitializationTask)
            ?? this.BeginContainerInitialization();

        try
        {
            await WaitForInitializationAsync(initializationTask, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (initializationTask.IsFaulted || initializationTask.IsCanceled)
            {
                // Don't cache a failed attempt (e.g. a transient/throttled storage error): allow
                // the next caller to retry initialization instead of failing forever. Use
                // CompareExchange so we don't clobber a newer task set by a racing caller.
                _ = Interlocked.CompareExchange(ref this.containerInitializationTask, null, initializationTask);
            }
        }
    }

    /// <summary>
    /// Starts container initialization if it hasn't already started, in a single-flight manner:
    /// only the first caller's task is stored and returned to all callers, including ones racing
    /// concurrently on other threads.
    /// </summary>
    Task BeginContainerInitialization()
    {
        Task newInitializationTask = this.CreateContainerIfNotExistsAsync();
        return Interlocked.CompareExchange(ref this.containerInitializationTask, newInitializationTask, null)
            ?? newInitializationTask;
    }

    /// <summary>
    /// Creates the container if it doesn't already exist. This task may be shared by many
    /// concurrent callers, each with its own independently-cancellable <see cref="CancellationToken"/>;
    /// it intentionally does not use any single caller's token (see
    /// <see cref="WaitForInitializationAsync(Task, CancellationToken)"/>, which lets individual
    /// callers stop waiting without cancelling the shared operation for everyone else).
    /// </summary>
    async Task CreateContainerIfNotExistsAsync()
    {
        await this.containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: CancellationToken.None)
            .ConfigureAwait(false);
    }
}
