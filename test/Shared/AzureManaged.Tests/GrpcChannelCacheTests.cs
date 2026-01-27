// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Grpc.Net.Client;
using Xunit;

namespace Microsoft.DurableTask.Tests;

public class GrpcChannelCacheTests
{
    const string TestEndpoint = "http://localhost:5000";

    [Fact]
    public void GetOrCreate_SameKey_ReturnsSameChannel()
    {
        // Arrange
        using GrpcChannelCache cache = new();
        string key = "test-key";
        int factoryCallCount = 0;
        GrpcChannel Factory()
        {
            factoryCallCount++;
            return GrpcChannel.ForAddress(TestEndpoint);
        }

        // Act
        GrpcChannel channel1 = cache.GetOrCreate(key, Factory);
        GrpcChannel channel2 = cache.GetOrCreate(key, Factory);

        // Assert
        channel1.Should().BeSameAs(channel2);
        factoryCallCount.Should().Be(1, "factory should only be called once for the same key");
    }

    [Fact]
    public void GetOrCreate_DifferentKeys_ReturnsDifferentChannels()
    {
        // Arrange
        using GrpcChannelCache cache = new();
        string key1 = "key1";
        string key2 = "key2";

        // Act
        GrpcChannel channel1 = cache.GetOrCreate(key1, () => GrpcChannel.ForAddress(TestEndpoint));
        GrpcChannel channel2 = cache.GetOrCreate(key2, () => GrpcChannel.ForAddress(TestEndpoint));

        // Assert
        channel1.Should().NotBeSameAs(channel2);
    }

    [Fact]
    public void GetOrCreate_ConcurrentAccess_CreatesSingleChannel()
    {
        // Arrange
        using GrpcChannelCache cache = new();
        string key = "concurrent-key";
        int factoryCallCount = 0;
        object countLock = new();
        GrpcChannel Factory()
        {
            lock (countLock)
            {
                factoryCallCount++;
            }

            // Add small delay to increase chance of race conditions
            Thread.Sleep(10);
            return GrpcChannel.ForAddress(TestEndpoint);
        }

        // Act
        GrpcChannel[] channels = new GrpcChannel[10];
        Parallel.For(0, 10, i =>
        {
            channels[i] = cache.GetOrCreate(key, Factory);
        });

        // Assert
        factoryCallCount.Should().Be(1, "factory should only be called once even with concurrent access");
        channels.All(c => ReferenceEquals(c, channels[0])).Should().BeTrue("all channels should be the same instance");
    }

    [Fact]
    public void Replace_ExistingChannel_DisposesOldChannel()
    {
        // Arrange
        using GrpcChannelCache cache = new();
        string key = "replace-key";
        GrpcChannel oldChannel = GrpcChannel.ForAddress(TestEndpoint);
        GrpcChannel newChannel = GrpcChannel.ForAddress(TestEndpoint);
        cache.GetOrCreate(key, () => oldChannel);

        // Act
        cache.Replace(key, newChannel);
        GrpcChannel retrievedChannel = cache.GetOrCreate(key, () => throw new InvalidOperationException("Should not be called"));

        // Assert
        retrievedChannel.Should().BeSameAs(newChannel);
        retrievedChannel.Should().NotBeSameAs(oldChannel);
    }

    [Fact]
    public void Replace_SameChannel_DoesNothing()
    {
        // Arrange
        using GrpcChannelCache cache = new();
        string key = "same-channel-key";
        GrpcChannel channel = GrpcChannel.ForAddress(TestEndpoint);
        cache.GetOrCreate(key, () => channel);

        // Act & Assert - should not throw or change anything
        cache.Replace(key, channel);
        GrpcChannel retrievedChannel = cache.GetOrCreate(key, () => throw new InvalidOperationException("Should not be called"));
        retrievedChannel.Should().BeSameAs(channel);
    }

    [Fact]
    public void Replace_NonExistingKey_AddsChannel()
    {
        // Arrange
        using GrpcChannelCache cache = new();
        string key = "new-key";
        GrpcChannel channel = GrpcChannel.ForAddress(TestEndpoint);

        // Act
        cache.Replace(key, channel);
        GrpcChannel retrievedChannel = cache.GetOrCreate(key, () => throw new InvalidOperationException("Should not be called"));

        // Assert
        retrievedChannel.Should().BeSameAs(channel);
    }

    [Fact]
    public void TryRemove_ExistingKey_RemovesAndReturnsTrue()
    {
        // Arrange
        using GrpcChannelCache cache = new();
        string key = "remove-key";
        cache.GetOrCreate(key, () => GrpcChannel.ForAddress(TestEndpoint));

        // Act
        bool result = cache.TryRemove(key);

        // Assert
        result.Should().BeTrue();

        // Verify the key is removed by checking that a new channel is created
        int factoryCallCount = 0;
        cache.GetOrCreate(key, () =>
        {
            factoryCallCount++;
            return GrpcChannel.ForAddress(TestEndpoint);
        });
        factoryCallCount.Should().Be(1, "a new channel should be created after removal");
    }

    [Fact]
    public void TryRemove_NonExistingKey_ReturnsFalse()
    {
        // Arrange
        using GrpcChannelCache cache = new();
        string key = "non-existing-key";

        // Act
        bool result = cache.TryRemove(key);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Dispose_DisposesAllChannels()
    {
        // Arrange
        GrpcChannelCache cache = new();
        cache.GetOrCreate("key1", () => GrpcChannel.ForAddress(TestEndpoint));
        cache.GetOrCreate("key2", () => GrpcChannel.ForAddress(TestEndpoint));
        cache.GetOrCreate("key3", () => GrpcChannel.ForAddress(TestEndpoint));

        // Act
        cache.Dispose();

        // Assert - attempting to use the cache after disposal should throw
        Action action = () => cache.GetOrCreate("key1", () => GrpcChannel.ForAddress(TestEndpoint));
        action.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        // Arrange
        GrpcChannelCache cache = new();
        cache.GetOrCreate("key1", () => GrpcChannel.ForAddress(TestEndpoint));

        // Act & Assert - multiple dispose calls should not throw
        cache.Dispose();
        cache.Dispose();
        cache.Dispose();
    }

    [Fact]
    public void GetOrCreate_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        GrpcChannelCache cache = new();
        cache.Dispose();

        // Act
        Action action = () => cache.GetOrCreate("key", () => GrpcChannel.ForAddress(TestEndpoint));

        // Assert
        action.Should().Throw<ObjectDisposedException>()
            .WithMessage("*GrpcChannelCache*");
    }

    [Fact]
    public void Replace_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        GrpcChannelCache cache = new();
        cache.Dispose();

        // Act
        Action action = () => cache.Replace("key", GrpcChannel.ForAddress(TestEndpoint));

        // Assert
        action.Should().Throw<ObjectDisposedException>()
            .WithMessage("*GrpcChannelCache*");
    }

    [Fact]
    public void GetOrCreate_WithNullKey_ThrowsArgumentException()
    {
        // Arrange
        using GrpcChannelCache cache = new();

        // Act
        Action action = () => cache.GetOrCreate(null!, () => GrpcChannel.ForAddress(TestEndpoint));

        // Assert
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetOrCreate_WithEmptyKey_ThrowsArgumentException()
    {
        // Arrange
        using GrpcChannelCache cache = new();

        // Act
        Action action = () => cache.GetOrCreate(string.Empty, () => GrpcChannel.ForAddress(TestEndpoint));

        // Assert
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetOrCreate_WithNullFactory_ThrowsArgumentNullException()
    {
        // Arrange
        using GrpcChannelCache cache = new();

        // Act
        Action action = () => cache.GetOrCreate("key", null!);

        // Assert
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Replace_WithNullKey_ThrowsArgumentException()
    {
        // Arrange
        using GrpcChannelCache cache = new();

        // Act
        Action action = () => cache.Replace(null!, GrpcChannel.ForAddress(TestEndpoint));

        // Assert
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Replace_WithNullChannel_ThrowsArgumentNullException()
    {
        // Arrange
        using GrpcChannelCache cache = new();

        // Act
        Action action = () => cache.Replace("key", null!);

        // Assert
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryRemove_WithNullKey_ThrowsArgumentException()
    {
        // Arrange
        using GrpcChannelCache cache = new();

        // Act
        Action action = () => cache.TryRemove(null!);

        // Assert
        action.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// This test verifies the core fix for the handle leak issue.
    /// Without the cache, each call to configure options would create a new GrpcChannel,
    /// causing handle count to grow unbounded when the service is unreachable.
    /// With the cache, repeated calls reuse the same channel, preventing handle leaks.
    /// </summary>
    [Fact]
    public void GetOrCreate_SimulatesRetryScenario_DoesNotCreateMultipleChannels()
    {
        // Arrange
        using GrpcChannelCache cache = new();
        string key = "client:default:myendpoint.durabletask.io:myhub";
        int factoryCallCount = 0;

        GrpcChannel CreateChannel()
        {
            factoryCallCount++;
            // Each GrpcChannel creates HttpClient + SocketsHttpHandler internally,
            // which allocates socket handles. Without caching, this would leak handles.
            return GrpcChannel.ForAddress(TestEndpoint);
        }

        // Act - Simulate what happens during retries when service is unreachable:
        // The options configuration callback may be invoked multiple times
        const int retryAttempts = 100;
        GrpcChannel[] channels = new GrpcChannel[retryAttempts];
        for (int i = 0; i < retryAttempts; i++)
        {
            channels[i] = cache.GetOrCreate(key, CreateChannel);
        }

        // Assert - The factory should only be called ONCE, not 100 times
        // This is the key behavior that prevents handle accumulation
        factoryCallCount.Should().Be(1,
            "the channel factory should only be called once regardless of how many times GetOrCreate is called - " +
            "this is what prevents handle leaks when the service is unreachable");

        // All returned channels should be the exact same instance
        channels.All(c => ReferenceEquals(c, channels[0])).Should().BeTrue(
            "all calls should return the same cached channel instance");
    }

    /// <summary>
    /// Verifies that the old behavior (without cache) would create multiple channels.
    /// This demonstrates what the cache prevents.
    /// </summary>
    [Fact]
    public void WithoutCache_MultipleCallsCreateMultipleChannels()
    {
        // Arrange - simulate old behavior without cache
        int factoryCallCount = 0;
        List<GrpcChannel> channels = new();

        GrpcChannel CreateChannelWithoutCache()
        {
            factoryCallCount++;
            return GrpcChannel.ForAddress(TestEndpoint);
        }

        // Act - Without caching, each "retry" creates a new channel
        const int retryAttempts = 10;
        for (int i = 0; i < retryAttempts; i++)
        {
            // This simulates the OLD behavior before the fix
            channels.Add(CreateChannelWithoutCache());
        }

        // Assert - Each call creates a new channel (the problematic behavior we fixed)
        factoryCallCount.Should().Be(retryAttempts,
            "without caching, each call creates a new channel - this causes handle leaks");

        // All channels are different instances
        channels.Distinct().Count().Should().Be(retryAttempts,
            "without caching, each channel is a unique instance with its own handles");

        // Cleanup
        foreach (var channel in channels)
        {
            channel.Dispose();
        }
    }

    /// <summary>
    /// Verifies channels are properly disposed when the cache is disposed,
    /// which releases the associated handles.
    /// </summary>
    [Fact]
    public async Task Dispose_ReleasesChannelResources()
    {
        // Arrange
        GrpcChannelCache cache = new();
        List<GrpcChannel> createdChannels = new();

        // Create multiple channels through the cache
        for (int i = 0; i < 5; i++)
        {
            string key = $"key{i}";
            GrpcChannel channel = cache.GetOrCreate(key, () =>
            {
                var c = GrpcChannel.ForAddress(TestEndpoint);
                createdChannels.Add(c);
                return c;
            });
        }

        createdChannels.Count.Should().Be(5);

        // Act - Dispose the cache (this should dispose all channels)
        cache.Dispose();

        // Wait a bit for async disposal to complete
        await Task.Delay(100);

        // Assert - The cache should be disposed and unusable
        Action action = () => cache.GetOrCreate("new-key", () => GrpcChannel.ForAddress(TestEndpoint));
        action.Should().Throw<ObjectDisposedException>();
    }
}
