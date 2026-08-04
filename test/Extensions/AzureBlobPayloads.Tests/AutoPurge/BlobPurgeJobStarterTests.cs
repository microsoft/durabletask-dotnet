// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests.AutoPurge;

public class BlobPurgeJobStarterTests
{
    [Fact]
    public async Task StartAsync_WhenStoreCannotDelete_DoesNotStartJobOrTouchClient()
    {
        // Arrange - the registered store is not the blob store, so its DeleteAsync is unsupported. Every payload
        // would fail, so the starter must refuse to run rather than spin against the backend. AutoPurge is on so
        // the run gets past the opt-in gate and reaches the store-capability gate under test.
        Mock<IDurableTaskClientProvider> provider = new();
        BlobPurgeJobStarter starter = new(
            provider.Object,
            new NonDeletingPayloadStore(),
            OptionsFor(new LargePayloadStorageOptions { AutoPurge = true }),
            "test",
            new TestLogger<BlobPurgeJobStarter>());

        // Act
        await starter.StartAsync(CancellationToken.None);

        // Assert - the store gate short-circuits before the client is resolved, so the provider is never asked
        // for a client.
        provider.Verify(p => p.GetClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_WhenStoreIsBlobStore_DoesNotShortCircuit()
    {
        // Arrange - the real blob store with auto-purge enabled. UseDevelopmentStorage=true is a valid connection
        // string that constructs the store offline (no network I/O), so resolving it at host start is safe.
        BlobPayloadStore store = new(new LargePayloadStorageOptions("UseDevelopmentStorage=true"));
        Mock<IDurableTaskClientProvider> provider = new();
        provider.Setup(p => p.GetClient(It.IsAny<string>())).Returns(new Mock<DurableTaskClient>("test").Object);
        TestLogger<BlobPurgeJobStarter> logger = new();
        BlobPurgeJobStarter starter = new(
            provider.Object,
            store,
            OptionsFor(new LargePayloadStorageOptions("UseDevelopmentStorage=true") { AutoPurge = true }),
            "test",
            logger);

        // Act
        await starter.StartAsync(CancellationToken.None);

        // Assert - neither gate fired: no store-cannot-delete log, and the starter proceeded to resolve the
        // client and start its background ensure path (which runs on a background task, so timing is not
        // asserted).
        logger.Logs.Should().NotContain(entry => entry.Message.Contains("is not an Azure Blob payload store"));
        provider.Verify(p => p.GetClient(It.IsAny<string>()), Times.Once);

        // Cleanup - cancel the background ensure task so it does not outlive the test.
        await starter.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_WhenAutoPurgeDisabled_DoesNotResolveClientOrLog()
    {
        // Arrange - auto-purge is off. Even with a delete-capable blob store, the starter must no-op silently:
        // it is registered unconditionally, so the not-opted-in path is the common case and must not log or
        // resolve a client.
        BlobPayloadStore store = new(new LargePayloadStorageOptions("UseDevelopmentStorage=true"));
        Mock<IDurableTaskClientProvider> provider = new();
        TestLogger<BlobPurgeJobStarter> logger = new();
        BlobPurgeJobStarter starter = new(
            provider.Object,
            store,
            OptionsFor(new LargePayloadStorageOptions("UseDevelopmentStorage=true") { AutoPurge = false }),
            "test",
            logger);

        // Act
        await starter.StartAsync(CancellationToken.None);

        // Assert - returned before resolving a client and without logging anything at all.
        provider.Verify(p => p.GetClient(It.IsAny<string>()), Times.Never);
        logger.Logs.Should().BeEmpty();
    }

    static IOptionsMonitor<LargePayloadStorageOptions> OptionsFor(LargePayloadStorageOptions options)
    {
        Mock<IOptionsMonitor<LargePayloadStorageOptions>> monitor = new();
        monitor.Setup(m => m.Get(It.IsAny<string>())).Returns(options);
        return monitor.Object;
    }

    sealed class NonDeletingPayloadStore : PayloadStore
    {
        // DeleteAsync is intentionally NOT overridden: the base PayloadStore.DeleteAsync throws
        // NotSupportedException, which is exactly the "store cannot delete" configuration the starter refuses.
        public override Task<string> UploadAsync(string payLoad, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<string> DownloadAsync(string token, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override bool IsKnownPayloadToken(string value) => false;
    }
}
