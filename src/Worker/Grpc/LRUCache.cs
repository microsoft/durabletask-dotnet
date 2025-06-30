// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.Grpc;

/// <summary>
/// Represents a Least Recently Used (LRU) cache of key-value pairs with a fixed capacity and a stale eviction time.
/// If the cache is at capacity and a new item is added, the least recently used item will be evicted.
/// The cache will be periodicially checked for items that have not been accessed within some timeframe, and evict
/// these "stale" items.
/// </summary>
/// <typeparam name="TKey">The type of the key.</typeparam>
/// <typeparam name="TValue">The type of the value.</typeparam>
class LRUCache<TKey, TValue> : IDisposable where TKey : notnull
{
    const int MaximumPercentageOfCacheSizePerItem = 75; // Maximum percentage of the cache that a single item can take up.

    readonly int capacityInBytes;
    readonly int checkForStaleItemsPeriodInMilliseconds;
    readonly int staleEvectionTimeInMilliseconds;
    readonly Dictionary<TKey, LinkedListNode<(TKey Key, TimedEntryWithSize TimedEntryWithSize)>> cacheMap = [];
    readonly LinkedList<(TKey Key, TimedEntryWithSize TimedEntryWithSize)> lruList = new();
    readonly object cacheLock = new();
    readonly Timer staleEvictionTimer;

    int sizeInBytes;

    /// <summary>
    /// Initializes a new instance of the <see cref="LRUCache{TKey, TValue}"/> class with the specified capacity and stale eviction time.
    /// </summary>
    /// <param name="capacityInBytes">The size of the cache, in bytes.</param>
    /// <param name="checkForStaleItemsPeriodInMilliseconds">The amount of time between the evictions of stale items.</param>
    /// <param name="staleEvectionTimeInMilliseconds">The amount of time that key-value pairs remain in the cache before they are considered "stale"
    /// and evicted in the next check for stale items - a call to Put or TryGet on the key will reset the time.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if any of the parameters are less than or equal to 0.</exception>
    internal LRUCache(int capacityInBytes, int checkForStaleItemsPeriodInMilliseconds, int staleEvectionTimeInMilliseconds)
    {
        if (capacityInBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacityInBytes), "Capacity of the LRU cache must be greater than 0 bytes");
        }

        if (checkForStaleItemsPeriodInMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(checkForStaleItemsPeriodInMilliseconds), "The amount of time between checks for stale items must be greater than 0 milliseconds");
        }

        if (staleEvectionTimeInMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(staleEvectionTimeInMilliseconds), "The amount of time before an item is considered stale and eligible for eviction must be greater than 0 milliseconds");
        }

        this.capacityInBytes = capacityInBytes;
        this.sizeInBytes = 0;
        this.checkForStaleItemsPeriodInMilliseconds = checkForStaleItemsPeriodInMilliseconds;
        this.staleEvectionTimeInMilliseconds = staleEvectionTimeInMilliseconds;
        this.staleEvictionTimer = new(this.EvictStaleItems, null, this.staleEvectionTimeInMilliseconds, this.checkForStaleItemsPeriodInMilliseconds);
    }

    /// <summary>
    /// Disposes the LRU cache.
    /// </summary>
    public void Dispose()
    {
        this.staleEvictionTimer.Dispose();
    }

    /// <summary>
    /// Returns the number of bytes the cache is able to accommodate, calculated by subtracting the current size in bytes from the capacity in bytes.
    /// </summary>
    /// <returns>The number of bytes remaining free in the cache.</returns>
    internal int GetFreeSpaceInBytes()
    {
        lock (this.cacheLock)
        {
            return this.capacityInBytes - this.sizeInBytes;
        }
    }

    /// <summary>
    /// Checks if the cache contains this key.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns>True if the cache contains the key; otherwise false.</returns>
    internal bool ContainsKey(TKey key)
    {
        lock (this.cacheLock)
        {
            return this.cacheMap.ContainsKey(key);
        }
    }

    /// <summary>
    /// Attempts to retrieve the value associated with the specified key from the cache. If the key is in the cache, its value is assigned to the value parameter
    /// and the key is moved to the most recently used position in the cache and its eviction time is reset. If the key is not found, the default value for the type
    /// of the value parameter is assigned to the value parameter instead.
    /// </summary>
    /// <param name="key">The key of the value to retrieve.</param>
    /// <param name="valueWithSize">When this method returns, contains the value associated with the specified key and its size, if the key is found;
    /// otherwise, the default value for the type of the value parameter. This parameter is passed uninitialized.</param>
    /// <returns>True if the cache contains an entry with the specified key; otherwise, false.</returns>
    internal bool TryGetValueWithSize(TKey key, out (TValue? Value, int Size) valueWithSize)
    {
        lock (this.cacheLock)
        {
            if (this.cacheMap.TryGetValue(key, out LinkedListNode<(TKey Key, TimedEntryWithSize TimedEntryWithSize)>? node))
            {
                this.lruList.Remove(node);
                this.lruList.AddFirst(node);
                node.Value.TimedEntryWithSize.LastAccessed = DateTimeOffset.UtcNow;
                valueWithSize = (node.Value.TimedEntryWithSize.Value, node.Value.TimedEntryWithSize.SizeInBytes);
                return true;
            }

            valueWithSize = default;
            return false;
        }
    }

    /// <summary>
    /// Attempts to retrieve the value associated with the specified key from the cache. If the key is in the cache, its value is assigned to the value parameter
    /// and the key is moved to the most recently used position in the cache and its eviction time is reset. If the key is not found, the default value for the type
    /// of the value parameter is assigned to the value parameter instead.
    /// </summary>
    /// <param name="key">The key of the value to retrieve.</param>
    /// <param name="value">When this method returns, contains the value associated with the specified key, if the key is found;
    /// otherwise, the default value for the type of the value parameter. This parameter is passed uninitialized.</param>
    /// <returns>True if the cache contains an entry with the specified key; otherwise, false.</returns>
    internal bool TryGetValue(TKey key, out TValue? value)
    {
        bool keyFound = this.TryGetValueWithSize(key, out (TValue? Value, int Size) valueWithSize);
        value = valueWithSize.Value;
        return keyFound;
    }

    /// <summary>
    /// Adds a new key-value pair to the cache or updates the value of an existing key. If adding the item would exceed the cache's capacity, the least recently used items are
    /// removed until there is enough space for the new item. The key will be moved to the most recently used position in the cache and its eviction time reset.
    /// </summary>
    /// <param name="key">The key to add.</param>
    /// <param name="value">The value associated with the key to add.</param>
    /// <param name="itemSizeInBytes">The size of the item in bytes. If the item is larger than the capacity of the cache, it will not be added to the cache.</param>
    /// <returns>True if the item was successfully stored, false if it was too big (>= than <see cref="MaximumPercentageOfCacheSizePerItem"/> of the cache size <see cref="capacityInBytes"/>).</returns>
    internal bool Put(TKey key, TValue value, int itemSizeInBytes)
    {
        lock (this.cacheLock)
        {
            // If the item is too big, we do not store it to avoid thrashing
            if (itemSizeInBytes >= MaximumPercentageOfCacheSizePerItem / 100 * this.capacityInBytes)
            {
                return false;
            }

            // If the key already exists, we update the value and move it to the front of the list
            if (this.cacheMap.TryGetValue(key, out LinkedListNode<(TKey Key, TimedEntryWithSize TimedEntry)>? nodeToRemove))
            {
                this.lruList.Remove(nodeToRemove);
            }

            // If we will exceed capacity, keep removing the last nodes in the list (the least recently used items) until there is enough space.
            // If the item's size in bytes is larger than the capacity, we will not add it to the cache.
            else if (itemSizeInBytes <= this.capacityInBytes && this.sizeInBytes + itemSizeInBytes > this.capacityInBytes)
            {
                while (this.lruList.Last != null && this.sizeInBytes + itemSizeInBytes > this.capacityInBytes)
                {
                    LinkedListNode<(TKey Key, TimedEntryWithSize TimedEntryWithSize)> lastNode = this.lruList.Last;
                    this.cacheMap.Remove(lastNode.Value.Key);
                    this.sizeInBytes -= lastNode.Value.TimedEntryWithSize.SizeInBytes;
                    this.lruList.RemoveLast();
                }
            }

            var newNode = new LinkedListNode<(TKey Key, TimedEntryWithSize TimedEntry)>((key, new TimedEntryWithSize(value, DateTimeOffset.UtcNow, itemSizeInBytes)));
            this.sizeInBytes += itemSizeInBytes;
            this.lruList.AddFirst(newNode);
            this.cacheMap[key] = newNode;
            return true;
        }
    }

    /// <summary>
    /// Removes the key and its value from the cache. If the key is not found, this method does nothing.
    /// </summary>
    /// <param name="key">The key to remove from the cache.</param>
    internal void Remove(TKey key)
    {
        lock (this.cacheLock)
        {
            if (this.cacheMap.TryGetValue(key, out LinkedListNode<(TKey Key, TimedEntryWithSize TimedEntry)>? nodeToRemove))
            {
                this.sizeInBytes -= nodeToRemove.Value.TimedEntry.SizeInBytes;
                this.cacheMap.Remove(key);
                this.lruList.Remove(nodeToRemove);
            }
        }
    }

    void EvictStaleItems(object? state)
    {
        var now = DateTimeOffset.UtcNow;
        lock (this.cacheLock)
        {
            // Since the list is ordered from most recently used to least recently used, we can iterate from the end of the list until we reach an item that is not stale.
            // All items preceding it are guaranteed to not be stale as well.
            while (this.lruList.Last != null && (now - this.lruList.Last.Value.TimedEntryWithSize.LastAccessed).TotalMilliseconds >= this.staleEvectionTimeInMilliseconds)
            {
                this.sizeInBytes -= this.lruList.Last.Value.TimedEntryWithSize.SizeInBytes;
                this.cacheMap.Remove(this.lruList.Last.Value.Key);
                this.lruList.RemoveLast();
            }
        }
    }

    class TimedEntryWithSize(TValue value, DateTimeOffset lastAccessed, int sizeInBytes)
    {
        public TValue Value { get; set; } = value;

        public DateTimeOffset LastAccessed { get; set; } = lastAccessed;

        public int SizeInBytes { get; set; } = sizeInBytes;
    }
}
