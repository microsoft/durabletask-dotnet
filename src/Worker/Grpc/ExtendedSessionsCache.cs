// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.DurableTask.Worker.Grpc;

/// <summary>
/// A cache for extended sessions that wraps an <see cref="IMemoryCache"/> instance.
/// Responsible for holding <see cref="ExtendedSessionState"/> for orchestrations that are running within extended sessions.
/// </summary>
public class ExtendedSessionsCache
{
    IMemoryCache? extendedSessions;

    /// <summary>
    /// Gets the cache for extended sessions if it has already been initialized, or otherwise initializes it with the given expiration scan frequency.
    /// </summary>
    /// <param name="expirationScanFrequencyInSeconds">
    /// The expiration scan frequency of the cache, in seconds. T
    /// This specifies how often the cache checks for stale items, and evicts them.
    /// </param>
    /// <returns>The IMemoryCache that holds the cached <see cref="ExtendedSessionState"/>.</returns>
    internal IMemoryCache GetOrInitializeCache(double expirationScanFrequencyInSeconds)
    {
        this.extendedSessions ??= new MemoryCache(new MemoryCacheOptions
        {
            // To avoid overloading the system with too-frequent scans, with cap the scanning frequency at 3 seconds.
            ExpirationScanFrequency = TimeSpan.FromSeconds(Math.Max(expirationScanFrequencyInSeconds / 5, 3)),
        });

        return this.extendedSessions;
    }
}
