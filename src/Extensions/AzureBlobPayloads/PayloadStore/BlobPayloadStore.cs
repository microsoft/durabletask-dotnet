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

    /// <summary>
    /// A no-op fault-observer used as the default value of <see cref="OnInitializationFaultObserved"/>,
    /// so that field is never <see langword="null"/> and <see cref="ObserveFaultWithoutAwaiting(Task)"/>
    /// can invoke it unconditionally - see the remarks on <see cref="OnInitializationFaultObserved"/>
    /// for why this matters.
    /// </summary>
    static readonly Action<Exception> NoOpFaultObserver = static _ => { };

    readonly BlobContainerClient containerClient;
    readonly LargePayloadStorageOptions options;

    // Caches the single in-flight (or completed) container-initialization gate so that
    // concurrent/subsequent uploads don't each issue their own CreateIfNotExistsAsync request.
    // Null means "not yet attempted" or "needs to be retried". The Lazy<Task> wrapper makes
    // initialization truly single-flight: publishing the gate (a cheap CompareExchange) always
    // happens before any real work starts, and Lazy<T> guarantees the factory (which starts the
    // real CreateIfNotExistsAsync call) runs exactly once even if multiple callers race to
    // access Value concurrently. See PublishNewInitializer and CreateContainerIfNotExistsAsync.
    Lazy<Task>? containerInitializer;

    /// <summary>
    /// Backing field for <see cref="OnInitializationFaultObserved"/>. Never <see langword="null"/>;
    /// defaults to, and is reset back to, <see cref="NoOpFaultObserver"/>.
    /// </summary>
    Action<Exception> onInitializationFaultObserved = NoOpFaultObserver;

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

    /// <summary>
    /// Gets or sets a test-only hook, invoked exactly once per initialization attempt that
    /// ultimately faults, immediately after the fault-observing continuation attached in
    /// <see cref="PublishNewInitializer"/> reads the shared initializer's
    /// <see cref="Task.Exception"/> (see <see cref="ObserveFaultWithoutAwaiting(Task)"/>). This
    /// lets unit tests deterministically verify the continuation actually ran and observed the
    /// fault, instead of relying on <see cref="TaskScheduler.UnobservedTaskException"/> plus
    /// forced garbage collection - which is sensitive to GC/finalization timing, debugger
    /// attachment, and JIT optimizations, and so cannot reliably distinguish "the fix ran" from
    /// "the CLR just hasn't collected the task yet".
    /// <para>
    /// This property is never <see langword="null"/>: it defaults to a no-op delegate, and
    /// setting it to <see langword="null"/> resets it back to that no-op rather than making the
    /// backing field nullable. This is deliberate, not just a convenience - it lets
    /// <see cref="ObserveFaultWithoutAwaiting(Task)"/> invoke this hook directly, with no
    /// null-conditional operator, which in turn means the same statement runs whether a test has
    /// overridden this hook or not. That eliminates - by construction, not merely by test
    /// coverage - the historical bug where the continuation used
    /// <c>this.OnInitializationFaultObserved?.Invoke(t.Exception!)</c>: because <c>?.Invoke(...)</c>
    /// short-circuits and skips evaluating its argument when the target is <see langword="null"/>,
    /// that pattern silently never read <see cref="Task.Exception"/> in production (where the hook
    /// was <see langword="null"/> by default), even though every test that exercised it happened to
    /// set a non-null hook first and so could never observe the regression. With this hook
    /// guaranteed non-null, that class of bug cannot recur regardless of how the invocation is
    /// written at the call site, and a test that overrides this hook is exercising the exact same
    /// code as the unmodified production default - just substituting a different delegate
    /// instance for <see cref="NoOpFaultObserver"/>.
    /// </para>
    /// Each test uses its own <see cref="BlobPayloadStore"/> instance, so no reset between tests
    /// is needed. Callers of this hook must not assume any particular thread.
    /// </summary>
    internal Action<Exception> OnInitializationFaultObserved
    {
        get => this.onInitializationFaultObserved;
        set => this.onInitializationFaultObserved = value ?? NoOpFaultObserver;
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
            // Keep the specific initializer instance this upload used so ContainerNotFound
            // recovery below can invalidate it precisely (see the catch block).
            Lazy<Task> containerInitializer = await this.EnsureContainerExistsAsync(cancellationToken).ConfigureAwait(false);

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
            catch (RequestFailedException ex) when (
                retryAfterContainerNotFound &&
                ex.ErrorCode == BlobErrorCode.ContainerNotFound)
            {
                // The container existed when we last verified/created it but has since been deleted
                // (e.g. by an operator). Clear the cached initializer so this same upload can recreate
                // the container and retry once. CompareExchange against the specific initializer this
                // attempt used ensures a stale failure can never clobber a newer initializer already
                // published by another, faster-recovering concurrent upload that detected the same
                // deletion.
                _ = Interlocked.CompareExchange(ref this.containerInitializer, null, containerInitializer);
                retryAfterContainerNotFound = false;
                continue;
            }

            return EncodeToken(this.containerClient.Name, blobName);
        }
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
    /// across all concurrent/subsequent callers, and returns the specific initializer gate that
    /// was used. The result is cached for the lifetime of this instance once it completes
    /// successfully; a failed attempt self-heals (see <see cref="CreateContainerIfNotExistsAsync(Lazy{Task})"/>)
    /// so the next caller retries instead of observing a stale failure forever.
    /// </summary>
    async Task<Lazy<Task>> EnsureContainerExistsAsync(CancellationToken cancellationToken)
    {
        Lazy<Task> initializer = Volatile.Read(ref this.containerInitializer)
            ?? this.PublishNewInitializer();

        await WaitForInitializationAsync(initializer.Value, cancellationToken).ConfigureAwait(false);
        return initializer;
    }

    /// <summary>
    /// Atomically publishes a not-yet-started initialization gate, in a true single-flight
    /// manner: publishing the gate (a cheap <see cref="Interlocked"/> compare-exchange)
    /// always completes before any real work starts, so racing first-time callers can never
    /// each independently trigger their own <c>CreateIfNotExistsAsync</c> request. Only the
    /// single winning gate is stored, and <see cref="Lazy{T}"/> (with
    /// <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/>) guarantees its factory -
    /// which starts the real container-creation call - is invoked exactly once even when
    /// multiple callers race to access <see cref="Lazy{T}.Value"/> concurrently. The winning
    /// publisher also attaches a single fault-observer (see
    /// <see cref="ObserveFaultWithoutAwaiting(Task)"/>) to the shared task, once, up front -
    /// rather than leaving each individual caller responsible for observing failures on its own
    /// cancellation path - so a shared initialization failure is always observed regardless of
    /// how many (if any) callers are still waiting on it when it faults, on every target
    /// framework this library supports.
    /// </summary>
    Lazy<Task> PublishNewInitializer()
    {
        Lazy<Task>? initializer = null;
        initializer = new Lazy<Task>(
            () => this.CreateContainerIfNotExistsAsync(initializer!),
            LazyThreadSafetyMode.ExecutionAndPublication);

        Lazy<Task> published = Interlocked.CompareExchange(ref this.containerInitializer, initializer, null) ?? initializer;
        if (ReferenceEquals(published, initializer))
        {
            this.ObserveFaultWithoutAwaiting(published.Value);
        }

        return published;
    }

    /// <summary>
    /// Attaches a fire-and-forget continuation that reads <see cref="Task.Exception"/> if
    /// <paramref name="task"/> ultimately faults, marking that fault as "observed" without
    /// awaiting or blocking on the task. Used so a shared task's eventual failure is always
    /// observed even if every caller that was waiting on it stops doing so (e.g. because each
    /// caller's own <see cref="CancellationToken"/> fired first) - otherwise the runtime would
    /// report it via <see cref="TaskScheduler.UnobservedTaskException"/> once the task is
    /// garbage-collected. Also invokes <see cref="OnInitializationFaultObserved"/> (a no-op in
    /// production) so tests can deterministically confirm this continuation ran - it is invoked
    /// directly, with no null-conditional operator, because it is guaranteed non-null (see its
    /// remarks); this is what makes reading <see cref="Task.Exception"/> here unconditional in
    /// every configuration, not merely in the current source form of this method.
    /// </summary>
    void ObserveFaultWithoutAwaiting(Task task)
    {
        _ = task.ContinueWith(
            t => this.OnInitializationFaultObserved(t.Exception!),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Creates the container if it doesn't already exist. This task may be shared by many
    /// concurrent callers, each with its own independently-cancellable <see cref="CancellationToken"/>;
    /// it intentionally does not use any single caller's token (see
    /// <see cref="WaitForInitializationAsync(Task, CancellationToken)"/>, which lets individual
    /// callers stop waiting without cancelling the shared operation for everyone else).
    /// If the underlying call fails, this method self-heals the cache by clearing it as part of
    /// its own completion - independent of whether any particular caller is still around to
    /// observe the failure - so the next upload gets a fresh initialization attempt instead of a
    /// stale error. The CompareExchange only clears the cache if it still points at this same
    /// gate (<paramref name="self"/>), so this can never erase a newer initializer already
    /// published by a racing caller (e.g. one that recovered from a concurrently-deleted
    /// container) after this attempt started.
    /// </summary>
    async Task CreateContainerIfNotExistsAsync(Lazy<Task> self)
    {
        try
        {
            await this.containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            _ = Interlocked.CompareExchange(ref this.containerInitializer, null, self);
            throw;
        }
    }
}
