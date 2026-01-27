// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using Grpc.Net.Client;

namespace Microsoft.DurableTask;

/// <summary>
/// Thread-safe cache for gRPC channels that ensures channels are reused across retries/calls
/// and properly disposed when replaced or evicted.
/// </summary>
sealed class GrpcChannelCache : IDisposable
{
    readonly ConcurrentDictionary<string, GrpcChannel> channels = new();
    readonly object syncLock = new();
    volatile bool disposed;

    /// <summary>
    /// Gets or creates a cached gRPC channel for the specified key.
    /// If a channel already exists for the key, it is returned.
    /// If the factory creates a new channel, any existing channel for the key is disposed.
    /// </summary>
    /// <param name="key">The cache key (typically endpoint + taskhub combination).</param>
    /// <param name="channelFactory">Factory function to create a new channel if needed.</param>
    /// <returns>The cached or newly created gRPC channel.</returns>
    public GrpcChannel GetOrCreate(string key, Func<GrpcChannel> channelFactory)
    {
        Check.NotNullOrEmpty(key);
        Check.NotNull(channelFactory);

        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(GrpcChannelCache));
        }

        // Fast path: return existing channel
        if (this.channels.TryGetValue(key, out GrpcChannel? existingChannel))
        {
            return existingChannel;
        }

        // Create channel outside lock to avoid potential deadlock if factory calls back into cache
        GrpcChannel newChannel = channelFactory();

        lock (this.syncLock)
        {
            if (this.disposed)
            {
                // Cache was disposed while we were creating the channel - dispose and throw
                DisposeChannelAsync(newChannel);
                throw new ObjectDisposedException(nameof(GrpcChannelCache));
            }

            // Check if another thread added a channel while we were creating ours
            if (this.channels.TryGetValue(key, out existingChannel))
            {
                // Dispose our duplicate and return the existing one
                DisposeChannelAsync(newChannel);
                return existingChannel;
            }

            this.channels[key] = newChannel;
            return newChannel;
        }
    }

    /// <summary>
    /// Replaces an existing channel for the specified key with a new one,
    /// disposing the old channel if it exists.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="newChannel">The new channel to cache.</param>
    public void Replace(string key, GrpcChannel newChannel)
    {
        Check.NotNullOrEmpty(key);
        Check.NotNull(newChannel);

        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(GrpcChannelCache));
        }

        GrpcChannel? oldChannel = null;

        lock (this.syncLock)
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(GrpcChannelCache));
            }

            // Only replace if it's actually a different channel
            if (this.channels.TryGetValue(key, out oldChannel) &&
                ReferenceEquals(oldChannel, newChannel))
            {
                return;
            }

            this.channels[key] = newChannel;
        }

        // Dispose the old channel outside the lock to avoid potential deadlocks
        DisposeChannelAsync(oldChannel);
    }

    /// <summary>
    /// Removes and disposes a channel for the specified key.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <returns>True if a channel was removed; otherwise, false.</returns>
    public bool TryRemove(string key)
    {
        Check.NotNullOrEmpty(key);

        if (this.channels.TryRemove(key, out GrpcChannel? channel))
        {
            DisposeChannelAsync(channel);
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        lock (this.syncLock)
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;

            foreach (KeyValuePair<string, GrpcChannel> kvp in this.channels)
            {
                DisposeChannelAsync(kvp.Value);
            }

            this.channels.Clear();
        }
    }

    static void DisposeChannelAsync(GrpcChannel? channel)
    {
        if (channel == null)
        {
            return;
        }

        // ShutdownAsync is the graceful way to close a gRPC channel
        // We fire-and-forget but ensure the channel is eventually disposed
        _ = Task.Run(async () =>
        {
            using (channel)
            {
                try
                {
                    await channel.ShutdownAsync();
                }
                catch (Exception)
                {
                    // Ignore shutdown errors during disposal
                }
            }
        });
    }
}
