// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using DurableTask.Core;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.DurableTask.Worker;

public class ExtendedSessionsCacheTests
{
    static readonly FieldInfo OwnershipField = typeof(ExtendedSessionState)
        .GetField("ownership", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"{nameof(ExtendedSessionState)}.ownership was not found.");

    [Fact]
    public void TakeAndReinsert_StaleGenerationCannotDisposeFreshOwner()
    {
        // Arrange
        using var cache = new ExtendedSessionsCache();
        cache.GetOrInitializeCache(30);
        var orchestration = new CountingTaskOrchestration();
        var session = new ExtendedSessionState(null!, orchestration, null!);

        // Act
        cache.TryStoreExtendedSession("instance", session, TimeSpan.FromSeconds(30)).Should().BeTrue();
        long oldGeneration = GetOwnership(session);
        cache.TryTakeExtendedSession("instance", out ExtendedSessionState? taken).Should().BeTrue();
        taken.Should().BeSameAs(session);
        cache.TryStoreExtendedSession("instance", session, TimeSpan.FromSeconds(30)).Should().BeTrue();
        long freshGeneration = GetOwnership(session);

        session.DisposeCacheGeneration(oldGeneration);

        // Assert
        freshGeneration.Should().BeGreaterThan(oldGeneration);
        orchestration.DisposeCount.Should().Be(0);

        cache.RemoveCachedValue("instance");
        session.DisposeCacheGeneration(freshGeneration);
        session.DisposeCacheGeneration(oldGeneration);
        session.DisposeCacheGeneration(freshGeneration);
        orchestration.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void Store_WhenMemoryCacheThrows_ReturnsOwnershipToRunner()
    {
        // Arrange
        var cache = new ExtendedSessionsCache();
        MemoryCache memoryCache = cache.GetOrInitializeCache(30);
        memoryCache.Dispose();
        var orchestration = new CountingTaskOrchestration();
        var session = new ExtendedSessionState(null!, orchestration, null!);

        // Act
        Action store = () => cache.TryStoreExtendedSession(
            "instance",
            session,
            TimeSpan.FromSeconds(30));

        // Assert
        store.Should().Throw<ObjectDisposedException>();
        session.DisposeRunnerOwned();
        session.DisposeRunnerOwned();
        orchestration.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void Store_WhenSlidingExpirationIsRejected_RetainsRunnerOwnership()
    {
        // Arrange
        using var cache = new ExtendedSessionsCache();
        cache.GetOrInitializeCache(30);
        var orchestration = new CountingTaskOrchestration();
        var session = new ExtendedSessionState(null!, orchestration, null!);

        // Act
        Action store = () => cache.TryStoreExtendedSession(
            "instance",
            session,
            TimeSpan.Zero);

        // Assert
        store.Should().Throw<ArgumentOutOfRangeException>();
        session.DisposeRunnerOwned();
        session.DisposeRunnerOwned();
        orchestration.DisposeCount.Should().Be(1);
    }

    static long GetOwnership(ExtendedSessionState session)
    {
        return OwnershipField.GetValue(session) is long ownership
            ? ownership
            : throw new InvalidOperationException(
                $"{nameof(ExtendedSessionState)}.ownership was null or had an unexpected type.");
    }

    sealed class CountingTaskOrchestration : TaskOrchestration, IDisposable
    {
        int disposeCount;

        public int DisposeCount => Volatile.Read(ref this.disposeCount);

        public void Dispose() => Interlocked.Increment(ref this.disposeCount);

        public override Task<string?> Execute(OrchestrationContext context, string input)
            => throw new NotImplementedException();

        public override string? GetStatus() => throw new NotImplementedException();

        public override void RaiseEvent(OrchestrationContext context, string name, string input)
            => throw new NotImplementedException();
    }
}
