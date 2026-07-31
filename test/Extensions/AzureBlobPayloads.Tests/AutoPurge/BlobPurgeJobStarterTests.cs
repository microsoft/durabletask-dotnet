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
        // would fail, so the starter must refuse to run rather than spin against the backend.
        Mock<DurableTaskClient> client = new("test");
        BlobPurgeJobStarter starter = new(
            client.Object,
            new NonDeletingPayloadStore(),
            OptionsFor(new LargePayloadStorageOptions { AutoPurge = true }),
            "test",
            new TestLogger<BlobPurgeJobStarter>());

        // Act
        await starter.StartAsync(CancellationToken.None);

        // Assert - the startup gate short-circuits before scheduling anything, so the client is never queried.
        client.Verify(
            c => c.GetInstanceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_WhenStoreIsBlobStore_DoesNotShortCircuit()
    {
        // Arrange - the real blob store. UseDevelopmentStorage=true is a valid connection string that constructs
        // the store offline (no network I/O), so resolving it at host start is safe.
        BlobPayloadStore store = new(new LargePayloadStorageOptions("UseDevelopmentStorage=true"));
        Mock<DurableTaskClient> client = new("test");
        TestLogger<BlobPurgeJobStarter> logger = new();
        BlobPurgeJobStarter starter = new(
            client.Object,
            store,
            OptionsFor(new LargePayloadStorageOptions("UseDevelopmentStorage=true")),
            "test",
            logger);

        // Act
        await starter.StartAsync(CancellationToken.None);

        // Assert - the store-cannot-delete gate did NOT fire; the starter proceeded to its background ensure
        // path (which runs on a background task, so timing is not asserted).
        logger.Logs.Should().NotContain(entry => entry.Message.Contains("is not an Azure Blob payload store"));

        // Cleanup - cancel the background ensure task so it does not outlive the test.
        await starter.StopAsync(CancellationToken.None);
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
