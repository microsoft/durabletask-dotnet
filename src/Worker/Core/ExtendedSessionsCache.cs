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
