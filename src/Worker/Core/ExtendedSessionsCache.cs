// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// A cache for extended sessions that wraps a <see cref="MemoryCache"/> instance.
/// Responsible for holding <see cref="ExtendedSessionState"/> for orchestrations that are running within extended sessions.
/// </summary>
public class ExtendedSessionsCache : IDisposable
{
    MemoryCache? extendedSessions;
    int disposed;

    /// <summary>
    /// Gets a value indicating whether returns whether or not the cache has been initialized.
    /// </summary>
    /// <returns>True if the cache has been initialized, false otherwise.</returns>
    public bool IsInitialized => this.extendedSessions is not null;

    /// <summary>
    /// Dispose the cache and release all resources.
    /// </summary>
    public void Dispose()
    {
        if (System.Threading.Interlocked.Exchange(ref this.disposed, 1) != 0)
        {
            // Already disposed (or a concurrent Dispose() call won the race). MemoryCache.Clear()
            // and MemoryCache.Dispose() are not safe to call more than once -- Clear() throws
            // ObjectDisposedException if the cache has already been disposed -- so this guard makes
            // Dispose() idempotent and safe under concurrent callers.
            return;
        }

        // MemoryCache.Dispose() does NOT invoke post-eviction callbacks for entries that are still
        // present in the cache -- it merely tears down the cache's internal state. Any entries
        // (e.g. cached extended-session state holding an IDisposable shim) that are still cached at
        // shutdown would therefore never be disposed. Calling Clear() first forces every remaining
        // entry to be removed via the normal removal path, which does invoke eviction callbacks for
        // each entry, ensuring they are triggered instead of silently skipped. Note that eviction
        // callbacks are queued asynchronously (via Task.Factory.StartNew), so Clear() does not
        // guarantee those callbacks have completed by the time Dispose() returns -- it only
        // guarantees they are scheduled before the cache itself is torn down.
        this.extendedSessions?.Clear();
        this.extendedSessions?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Gets the cache for extended sessions if it has already been initialized, or otherwise initializes it with the given expiration scan frequency.
    /// </summary>
    /// <param name="expirationScanFrequencyInSeconds">
    /// The expiration scan frequency of the cache, in seconds.
    /// This specifies how often the cache checks for stale items, and evicts them.
    /// </param>
    /// <returns>The IMemoryCache that holds the cached <see cref="ExtendedSessionState"/>.</returns>
    public MemoryCache GetOrInitializeCache(double expirationScanFrequencyInSeconds)
    {
        this.extendedSessions ??= new MemoryCache(new MemoryCacheOptions
        {
            ExpirationScanFrequency = TimeSpan.FromSeconds(expirationScanFrequencyInSeconds / 5),
        });

        return this.extendedSessions;
    }
}
