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
/// <c>blob:v2:{fullBlobUrl}</c>, where the URL is the blob's absolute URI including the storage account.
/// Legacy <c>blob:v1:{container}:{blobName}</c> tokens are still recognized for read back-compatibility.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "SemaphoreSlim does not allocate a disposable resource unless AvailableWaitHandle is accessed.")]
public sealed class BlobPayloadStore : PayloadStore
{
    /// <summary>
    /// The prefix of legacy v1 payload tokens, which identify the container by name only and not the storage
    /// account. Auto-purge uses this to detect and skip v1 tokens.
    /// </summary>
    internal const string TokenPrefixV1 = "blob:v1:";

    /// <summary>
    /// The prefix of self-describing v2 payload tokens, which carry the blob's absolute URI including the
    /// storage account. Auto-purge uses this to tell a malformed v2 token (a protocol defect) apart from an
    /// unrecognized version prefix (a token written by a newer SDK).
    /// </summary>
    internal const string TokenPrefixV2 = "blob:v2:";

    /// <summary>
    /// The metadata name of the ownership marker written on every blob this store creates. Recognizing a
    /// token proves only that its text matches the store's grammar; the marker is what proves the store
    /// actually wrote the blob, so a customer's own blob is never deleted just because an orchestration
    /// referenced it. Azure requires metadata names to follow the naming rules for C# identifiers, so the
    /// marker is spelled with an underscore rather than a hyphen.
    /// </summary>
    internal const string OwnershipMarkerName = "managed_by";

    /// <summary>
    /// The fixed value of the ownership marker written on every blob this store creates.
    /// </summary>
    internal const string OwnershipMarkerValue = "dts";

    const string ContentEncodingGzip = "gzip";
    const int MaxRetryAttempts = 8;
    const int BaseDelayMs = 250;
    const int MaxDelayMs = 10_000;
    const int NetworkTimeoutMinutes = 2;

    readonly BlobContainerClient containerClient;
    readonly LargePayloadStorageOptions options;
    readonly BlobClientOptions clientOptions;
    readonly SemaphoreSlim containerInitializationLock = new(initialCount: 1, maxCount: 1);

    // Each successful initialization publishes a unique token. The token lets a stale
    // ContainerNotFound failure invalidate only the initialization generation it used.
    object? containerGeneration;

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
        this.clientOptions = new BlobClientOptions();
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
        // Retry one write after an out-of-band container deletion so the cached path preserves
        // the recovery behavior that an unconditional CreateIfNotExistsAsync provided before
        // initialization was cached.
        bool retryAfterContainerNotFound = true;
        while (true)
        {
            object generation = await this.EnsureContainerExistsAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (this.options.CompressionEnabled)
                {
                    BlobOpenWriteOptions writeOptions = new()
                    {
                        HttpHeaders = new BlobHttpHeaders { ContentEncoding = ContentEncodingGzip },
                        Metadata = CreateOwnershipMetadata(),
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
                    // The uncompressed path still needs write options purely to carry the ownership marker:
                    // the marker must be written by every path that creates a blob, or auto-purge would later
                    // decline to delete the store's own uncompressed payloads. It rides along in the PUT the
                    // upload already issues, so it costs no extra request.
                    BlobOpenWriteOptions writeOptions = new()
                    {
                        Metadata = CreateOwnershipMetadata(),
                    };
                    using Stream blobStream = await blob.OpenWriteAsync(true, writeOptions, cancellationToken);

                    // using MemoryStream payloadStream = new(payloadBuffer, writable: false);
                    // await payloadStream.CopyToAsync(blobStream, bufferSize: DefaultCopyBufferSize, cancellationToken);
                    await WritePayloadAsync(payloadBuffer, blobStream, cancellationToken);
                    await blobStream.FlushAsync(cancellationToken);
                }
            }
            catch (RequestFailedException ex) when (
                retryAfterContainerNotFound &&
                ex.ErrorCode == BlobErrorCode.ContainerNotFound)
            {
                // Clear only the generation used by this upload, then retry once. If Azure is still
                // deleting the container, CreateIfNotExistsAsync can surface ContainerBeingDeleted,
                // matching the behavior before initialization was cached.
                _ = Interlocked.CompareExchange(ref this.containerGeneration, null, generation);
                retryAfterContainerNotFound = false;
                continue;
            }

            return EncodeToken(blob.Uri);
        }
    }

    /// <inheritdoc/>
    public override async Task<string> DownloadAsync(string token, CancellationToken cancellationToken)
    {
        DecodeTokenResult decoded = DecodeToken(token);

        if (!decoded.IsV2)
        {
            // v1 tokens do not carry the account, so the payload is assumed to live in the configured container.
            if (!string.Equals(decoded.Container, this.containerClient.Name, StringComparison.Ordinal))
            {
                throw new ArgumentException("Token container does not match configured container.", nameof(token));
            }

            return await DownloadFromBlobAsync(this.containerClient.GetBlobClient(decoded.Name), cancellationToken);
        }

        // v2 tokens are self-describing: honor the account and container encoded in the token.
        BlobClient blob;
        if (this.IsConfiguredContainer(decoded.ContainerUri!))
        {
            // Same account and container as the configured store: reuse it (works with any auth mode).
            blob = this.containerClient.GetBlobClient(decoded.Name);
        }
        else if (this.options.Credential != null)
        {
            // The payload lives in a different account (e.g. the store was repointed). Identity auth can still
            // read it as long as the credential has RBAC access to that account.
            blob = new BlobClient(decoded.BlobUri, this.options.Credential, this.clientOptions);
        }
        else
        {
            throw new PayloadStorageException(
                $"The externalized payload lives in a different storage account ('{decoded.ContainerUri}') than the " +
                $"currently-configured payload store ('{this.containerClient.Uri}'). Cross-account payload reads " +
                "require identity (AAD) authentication with access to both accounts; connection-string / " +
                "account-key credentials are account-specific and cannot read another account.");
        }

        return await DownloadFromBlobAsync(blob, cancellationToken);
    }

    /// <inheritdoc/>
    public override async Task<PayloadDeleteOutcome> DeleteAsync(string token, CancellationToken cancellationToken)
    {
        DecodeTokenResult decoded = DecodeToken(token);

        BlobClient blob;
        if (!decoded.IsV2)
        {
            // v1 tokens do not carry the account, so the payload is assumed to live in the configured container.
            if (!string.Equals(decoded.Container, this.containerClient.Name, StringComparison.Ordinal))
            {
                throw new ArgumentException("Token container does not match configured container.", nameof(token));
            }

            blob = this.containerClient.GetBlobClient(decoded.Name);
        }
        else if (this.IsConfiguredContainer(decoded.ContainerUri!))
        {
            // Same account and container as the configured store: reuse it (works with any auth mode).
            blob = this.containerClient.GetBlobClient(decoded.Name);
        }
        else if (this.options.Credential != null)
        {
            // The payload lives in a different account (e.g. the store was repointed). Identity auth can still
            // delete it as long as the credential has RBAC access to that account.
            blob = new BlobClient(decoded.BlobUri, this.options.Credential, this.clientOptions);
        }
        else
        {
            throw new PayloadStorageException(
                $"The externalized payload lives in a different storage account ('{decoded.ContainerUri}') than the " +
                $"currently-configured payload store ('{this.containerClient.Uri}'). Cross-account payload deletes " +
                "require identity (AAD) authentication with access to both accounts; connection-string / " +
                "account-key credentials are account-specific and cannot delete in another account.");
        }

        // Recognizing the token proves only that its text matches this store's grammar - not that this store
        // wrote the blob. A customer may keep an expensive dataset in Blob Storage and have orchestrations
        // reference it by URL; deleting that would destroy data the store never created. So ownership is read
        // from the object itself before anything is deleted.
        BlobProperties properties;
        try
        {
            Response<BlobProperties> response = await blob.GetPropertiesAsync(
                conditions: null, cancellationToken: cancellationToken);
            properties = response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            // Already gone. Deletion is idempotent, so a re-delivered tombstone or a concurrent worker
            // replica that won the race is a success, not an error.
            return PayloadDeleteOutcome.AlreadyAbsent;
        }

        if (!HasOwnershipMarker(properties.Metadata))
        {
            // Positive evidence that the blob is customer-owned: leave it untouched. The caller still resolves
            // the payload reference, because a blob this store never wrote is not this store's to delete.
            return PayloadDeleteOutcome.NotStoreOwned;
        }

        // Pair the ownership read with the delete using the ETag from that same read. If anything rewrites the
        // blob in between - including a customer overwriting it with content that no longer carries the marker
        // - the If-Match condition fails the delete instead of removing the newer content, so the read-then-
        // delete behaves as a single check-and-delete without taking a lease.
        // Idempotent by design: DeleteIfExistsAsync returns false (rather than throwing) when the blob is
        // already gone, so re-delivered tombstones and concurrent purges from multiple worker replicas are safe.
        // IncludeSnapshots removes the blob's snapshots along with the base blob, but it does NOT delete blob
        // *versions*: with versioning enabled, deleting the base blob turns the current version into a retained
        // previous version. Versions are deliberately not enumerated and deleted here - doing so still would not
        // guarantee the bytes are reclaimed, because soft delete is a container-level policy that retains deleted
        // content for its retention period regardless of how the delete was issued. Immediate reclamation is
        // therefore unobtainable client-side and belongs to a lifecycle-management policy or the retention
        // expiry, so Deleted means "accepted by storage", not "bytes reclaimed" (see PayloadDeleteOutcome.Deleted).
        Response<bool> deleted = await blob.DeleteIfExistsAsync(
            DeleteSnapshotsOption.IncludeSnapshots,
            conditions: new BlobRequestConditions { IfMatch = properties.ETag },
            cancellationToken: cancellationToken);

        return deleted.Value ? PayloadDeleteOutcome.Deleted : PayloadDeleteOutcome.AlreadyAbsent;
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
    /// <returns>An opaque payload token in the form <c>blob:v2:{fullBlobUrl}</c>.</returns>
    internal static string EncodeToken(Uri blobUri) => $"{TokenPrefixV2}{blobUri}";

    /// <summary>
    /// Decodes a payload token. Supports self-describing v2 tokens (<c>blob:v2:{fullBlobUrl}</c>) and legacy v1
    /// tokens (<c>blob:v1:{container}:{blobName}</c>), the latter for read back-compatibility.
    /// </summary>
    /// <param name="token">The payload token to decode.</param>
    /// <returns>
    /// A <see cref="DecodeTokenResult"/> describing the token: whether it is v2, the container and blob names,
    /// and (for v2 only) the absolute blob URI and its container-level URI.
    /// </returns>
    internal static DecodeTokenResult DecodeToken(string token)
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
            return new(true, container, name, blobUri, containerUri);
        }

        if (token.StartsWith(TokenPrefixV1, StringComparison.Ordinal))
        {
            string rest = token.Substring(TokenPrefixV1.Length);
            int sep = rest.IndexOf(':');
            if (sep <= 0 || sep >= rest.Length - 1)
            {
                throw new ArgumentException("Invalid external payload token format.", nameof(token));
            }

            return new(false, rest.Substring(0, sep), rest.Substring(sep + 1), null, null);
        }

        throw new ArgumentException("Invalid external payload token.", nameof(token));
    }

    /// <summary>
    /// Creates the ownership metadata stamped on every blob this store writes, so a later purge can prove the
    /// store created the blob before deleting it.
    /// </summary>
    static Dictionary<string, string> CreateOwnershipMetadata() =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [OwnershipMarkerName] = OwnershipMarkerValue,
        };

    /// <summary>
    /// Returns whether the supplied blob metadata carries this store's ownership marker. Azure treats metadata
    /// names as case-insensitive, so the lookup is too.
    /// </summary>
    static bool HasOwnershipMarker(IDictionary<string, string>? metadata)
    {
        if (metadata is null)
        {
            return false;
        }

        foreach (KeyValuePair<string, string> entry in metadata)
        {
            if (string.Equals(entry.Key, OwnershipMarkerName, StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(entry.Value, OwnershipMarkerValue, StringComparison.Ordinal);
            }
        }

        return false;
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

    /// <summary>
    /// Ensures the container exists and returns the initialization generation used by the caller.
    /// </summary>
    async Task<object> EnsureContainerExistsAsync(CancellationToken cancellationToken)
    {
        object? generation = Volatile.Read(ref this.containerGeneration);
        if (generation is not null)
        {
            return generation;
        }

        await this.containerInitializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            generation = Volatile.Read(ref this.containerGeneration);
            if (generation is not null)
            {
                return generation;
            }

            await this.containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            generation = new object();
            Volatile.Write(ref this.containerGeneration, generation);
            return generation;
        }
        finally
        {
            this.containerInitializationLock.Release();
        }
    }

    /// <summary>
    /// The result of decoding an externalized payload token.
    /// </summary>
    /// <param name="IsV2">Whether the token is a self-describing v2 token.</param>
    /// <param name="Container">The name of the container holding the payload.</param>
    /// <param name="Name">The name of the blob holding the payload.</param>
    /// <param name="BlobUri">
    /// The absolute URI of the blob holding the payload, or <see langword="null"/> for v1 tokens, which do not
    /// carry the storage account.
    /// </param>
    /// <param name="ContainerUri">
    /// The container-level URI of <paramref name="BlobUri"/>, or <see langword="null"/> for v1 tokens.
    /// </param>
    internal readonly record struct DecodeTokenResult(
        bool IsV2, string Container, string Name, Uri? BlobUri, Uri? ContainerUri);
}
