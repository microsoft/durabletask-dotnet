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
    // Guards both the lazy-initialization of `extendedSessions` in GetOrInitializeCache() and the
    // disposal state transition in Dispose(). Without this shared lock, Dispose() could observe
    // `extendedSessions` as null (because it hasn't been lazily created yet), mark itself disposed,
    // and return -- while a concurrent GetOrInitializeCache() call races in and constructs a brand
    // new MemoryCache immediately afterwards. That cache would never be disposed (Dispose() has
    // already run and is now a permanent no-op), leaking it and any entries added to it. The lock
    // makes initialization and disposal mutually exclusive, so there's no window where a cache can
    // be created after (or concurrently with) disposal.
    readonly object syncRoot = new();

    MemoryCache? extendedSessions;
    bool disposed;

    /// <summary>
    /// Gets a value indicating whether the cache has been initialized.
    /// </summary>
    public bool IsInitialized => this.extendedSessions is not null;

    /// <summary>
    /// Dispose the cache and release all resources.
    /// </summary>
    public void Dispose()
    {
        MemoryCache? cacheToDispose;
        lock (this.syncRoot)
        {
            if (this.disposed)
            {
                // Already disposed by a previous (or concurrent, now-completed) call. MemoryCache.Clear()
                // and MemoryCache.Dispose() are not safe to call more than once -- Clear() throws
                // ObjectDisposedException if the cache has already been disposed -- so this guard makes
                // Dispose() idempotent and safe under concurrent callers.
                return;
            }

            this.disposed = true;

            // Clear the field (under the same lock used by GetOrInitializeCache()) so that no caller
            // can observe or lazily recreate a cache after this point; GetOrInitializeCache() checks
            // `this.disposed` under the lock and throws ObjectDisposedException instead.
            cacheToDispose = this.extendedSessions;
            this.extendedSessions = null;
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
        cacheToDispose?.Clear();
        cacheToDispose?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Gets the cache for extended sessions if it has already been initialized, or otherwise initializes it
    /// with an expiration scan frequency derived from the supplied duration.
    /// </summary>
    /// <remarks>
    /// The returned cache remains owned by this instance and must not be disposed by callers.
    /// </remarks>
    /// <param name="expirationScanFrequencyInSeconds">
    /// The duration, in seconds, used to derive the expiration scan frequency. The cache checks for stale
    /// items every one-fifth of this duration.
    /// </param>
    /// <returns>The IMemoryCache that holds the cached <see cref="ExtendedSessionState"/>.</returns>
    /// <exception cref="ObjectDisposedException">The cache has already been disposed.</exception>
    public MemoryCache GetOrInitializeCache(double expirationScanFrequencyInSeconds)
    {
        lock (this.syncRoot)
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(ExtendedSessionsCache));
            }

            this.extendedSessions ??= new MemoryCache(new MemoryCacheOptions
            {
                ExpirationScanFrequency = TimeSpan.FromSeconds(expirationScanFrequencyInSeconds / 5),
            });

            return this.extendedSessions;
        }
    }

    /// <summary>
    /// Attempts to retrieve the cached value for the given key, if present and this cache has not been
    /// disposed (nor is concurrently being disposed by another thread). Callers should use this instead
    /// of calling <see cref="IMemoryCache.TryGetValue(object, out object)"/> directly on the
    /// <see cref="MemoryCache"/> returned by <see cref="GetOrInitializeCache"/>, since this method is
    /// synchronized with <see cref="Dispose"/> and therefore can never observe -- or throw from -- a
    /// cache instance that is concurrently being torn down.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="value">When this method returns, contains the cached value, if found.</param>
    /// <returns><c>true</c> if a value was found; <c>false</c> if not found, or if this cache is disposed.</returns>
    internal bool TryGetCachedValue<T>(string key, out T? value)
    {
        lock (this.syncRoot)
        {
            if (this.disposed || this.extendedSessions is null)
            {
                value = default;
                return false;
            }

            return this.extendedSessions.TryGetValue(key, out value);
        }
    }

    /// <summary>
    /// Atomically removes an extended session from the cache and transfers ownership to the caller.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="value">When this method returns, contains the runner-owned session, if found.</param>
    /// <returns><c>true</c> if ownership was transferred; otherwise, <c>false</c>.</returns>
    internal bool TryTakeExtendedSession(string key, out ExtendedSessionState? value)
    {
        lock (this.syncRoot)
        {
            if (this.disposed
                || this.extendedSessions is null
                || !this.extendedSessions.TryGetValue(key, out value)
                || value is null)
            {
                value = null;
                return false;
            }

            if (!value.TryTakeFromCache(out _))
            {
                this.extendedSessions.Remove(key);
                value = null;
                return false;
            }

            try
            {
                // Transfer ownership before removing the entry. MemoryCache queues callbacks
                // asynchronously, so the callback for the old generation will now be harmless.
                this.extendedSessions.Remove(key);
                return true;
            }
            catch
            {
                // The session is no longer safely cache-owned. Dispose the runner lease rather than
                // risk either leaking it or returning a state that may still be reachable from the cache.
                value.DisposeRunnerOwned();
                value = null;
                throw;
            }
        }
    }

    /// <summary>
    /// Removes the cached value for the given key, if present. This is a safe no-op if this cache has
    /// already been disposed (or is concurrently being disposed). Synchronized with <see cref="Dispose"/>
    /// for the same reason as <see cref="TryGetCachedValue{T}(string, out T)"/>.
    /// </summary>
    /// <param name="key">The cache key to remove.</param>
    internal void RemoveCachedValue(string key)
    {
        lock (this.syncRoot)
        {
            if (this.disposed || this.extendedSessions is null)
            {
                return;
            }

            this.extendedSessions.Remove(key);
        }
    }

    /// <summary>
    /// Attempts to insert or replace the cached value for the given key. Returns <c>false</c> without
    /// modifying the cache if this <see cref="ExtendedSessionsCache"/> has already been disposed, or is
    /// concurrently being disposed by another thread -- in which case the caller retains ownership of
    /// <paramref name="value"/> (and remains responsible for disposing it, if applicable) instead of
    /// assuming the cache accepted it and will eventually evict and dispose it via a post-eviction
    /// callback. Synchronized with <see cref="Dispose"/> so there is no window in which an entry can be
    /// inserted after disposal has begun tearing the cache down (e.g. after <c>Clear()</c> has already
    /// run but before the underlying <see cref="MemoryCache"/> itself has been disposed) -- an insertion
    /// that would otherwise never be evicted or disposed again.
    /// </summary>
    /// <typeparam name="T">The type of the value to cache.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The value to cache.</param>
    /// <param name="options">The cache entry options (e.g. sliding expiration, eviction callback).</param>
    /// <returns><c>true</c> if the value was inserted; <c>false</c> if rejected because this cache is disposed.</returns>
    internal bool TrySetCachedValue<T>(string key, T value, MemoryCacheEntryOptions options)
    {
        lock (this.syncRoot)
        {
            if (this.disposed || this.extendedSessions is null)
            {
                return false;
            }

            this.extendedSessions.Set(key, value, options);
            return true;
        }
    }

    /// <summary>
    /// Atomically transfers a runner-owned extended session into the cache using a fresh ownership generation.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The runner-owned extended session.</param>
    /// <param name="slidingExpiration">The sliding expiration for the cache entry.</param>
    /// <returns>
    /// <c>true</c> if ownership was transferred; <c>false</c> if the cache rejected the transfer.
    /// </returns>
    internal bool TryStoreExtendedSession(
        string key,
        ExtendedSessionState value,
        TimeSpan slidingExpiration)
    {
        lock (this.syncRoot)
        {
            if (this.disposed || this.extendedSessions is null)
            {
                return false;
            }

            // Validate all caller-controlled options before transferring ownership. In particular,
            // a sub-tick positive timeout rounds to TimeSpan.Zero, which SlidingExpiration rejects.
            MemoryCacheEntryOptions options = new()
            {
                SlidingExpiration = slidingExpiration,
            };

            if (!value.TryTransferToCache(out long generation))
            {
                return false;
            }

            try
            {
                options.RegisterPostEvictionCallback(DisposeEvictedExtendedSession, generation);
                this.extendedSessions.Set(key, value, options);
                return true;
            }
            catch
            {
                // Set normally either commits or throws before insertion. Defensively handle an
                // implementation that commits and then throws by removing this exact value first.
                // Its queued callback and the ownership transfer below race safely on the generation.
                try
                {
                    if (this.extendedSessions.TryGetValue(key, out object? cachedValue)
                        && ReferenceEquals(cachedValue, value))
                    {
                        this.extendedSessions.Remove(key);
                    }
                }
                catch (ObjectDisposedException)
                {
                    // The publicly exposed MemoryCache may have been disposed directly. It no longer
                    // owns a usable entry, so ownership can still be returned to the runner below.
                }

                value.TryTakeCacheGeneration(generation);
                throw;
            }
        }
    }

    static void DisposeEvictedExtendedSession(object key, object? value, EvictionReason reason, object? state)
    {
        if (value is ExtendedSessionState sessionState && state is long generation)
        {
            sessionState.DisposeCacheGeneration(generation);
        }
    }
}
