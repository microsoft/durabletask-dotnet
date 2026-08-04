// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure;
using FluentAssertions;
using Microsoft.DurableTask.AzureBlobPayloads;
using Xunit;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests.AutoPurge;

public class DeleteExternalBlobActivityTests
{
    [Fact]
    public async Task RunAsync_WhenDeleteThrowsRequestFailed400_DiscardsPoisonToken()
    {
        // Arrange - a Status 400 (e.g. InvalidResourceName) is a permanent service rejection.
        StubPayloadStore store = new(new RequestFailedException(400, "InvalidResourceName"));
        DeleteExternalBlobActivity activity = new(store, new TestLogger<DeleteExternalBlobActivity>());

        // Act
        BlobDeleteResult result = await activity.RunAsync(null!, "blob:v2:https://acct.blob.core.windows.net/payloads/bad name");

        // Assert - discarded so the backend acks and clears the row instead of re-streaming forever.
        result.Should().Be(BlobDeleteResult.Discarded);
    }

    [Fact]
    public async Task RunAsync_WhenDeleteThrowsRequestFailedNon400_LeavesTombstonedForRetry()
    {
        // Arrange - a Status 503 that escaped the SDK's internal retries is treated as transient.
        StubPayloadStore store = new(new RequestFailedException(503, "ServerBusy"));
        DeleteExternalBlobActivity activity = new(store, new TestLogger<DeleteExternalBlobActivity>());

        // Act
        BlobDeleteResult result = await activity.RunAsync(null!, "blob:v2:https://acct.blob.core.windows.net/payloads/abc123");

        // Assert - left tombstoned so a later purge cycle can retry; a blob is never dropped on doubt.
        result.Should().Be(BlobDeleteResult.Retry);
    }

    [Fact]
    public async Task RunAsync_WhenDeleteThrowsPayloadStorageException_DiscardsToUnblockPipeline()
    {
        // Arrange - the payload lives in a storage account the configured credential cannot reach. Retrying can
        // never succeed and the backend batch is cursor-less, so a permanently unreachable row would re-stream
        // every cycle and block later rows; it must be discarded (acked), not retried.
        StubPayloadStore store = new(new PayloadStorageException("cross-account delete requires identity auth"));
        DeleteExternalBlobActivity activity = new(store, new TestLogger<DeleteExternalBlobActivity>());

        // Act
        BlobDeleteResult result = await activity.RunAsync(null!, "blob:v2:https://other.blob.core.windows.net/c/abc123");

        // Assert - discarded so the pipeline head-of-line is not blocked by an undeletable payload.
        result.Should().Be(BlobDeleteResult.Discarded);
    }

    [Fact]
    public async Task RunAsync_V1Token_DiscardsWithoutCallingStore()
    {
        // Arrange - auto-purge policy: a legacy v1 token identifies no storage account, so it is dropped before
        // the store is ever consulted.
        Mock<PayloadStore> store = new();
        DeleteExternalBlobActivity activity = new(store.Object, new TestLogger<DeleteExternalBlobActivity>());

        // Act
        BlobDeleteResult result = await activity.RunAsync(null!, "blob:v1:payloads:abc123");

        // Assert - discarded by the gate, and the store's DeleteAsync was never invoked.
        result.Should().Be(BlobDeleteResult.Discarded);
        store.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_V2Token_CallsStore()
    {
        // Arrange - a self-describing v2 token is not gated and must reach the store.
        Mock<PayloadStore> store = new();
        store.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        DeleteExternalBlobActivity activity = new(store.Object, new TestLogger<DeleteExternalBlobActivity>());

        // Act
        BlobDeleteResult result = await activity.RunAsync(null!, "blob:v2:https://acct.blob.core.windows.net/payloads/abc123");

        // Assert - the store deleted the blob (proves the gate is v1-only and did not break the happy path).
        result.Should().Be(BlobDeleteResult.Deleted);
        store.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenStoreDoesNotSupportDelete_RetriesToPreserveTombstone()
    {
        // Arrange - a store that cannot delete (the base PayloadStore.DeleteAsync throws NotSupportedException).
        StubPayloadStore store = new(new NotSupportedException());
        DeleteExternalBlobActivity activity = new(store, new TestLogger<DeleteExternalBlobActivity>());

        // Act
        BlobDeleteResult result = await activity.RunAsync(null!, "blob:v2:https://acct.blob.core.windows.net/payloads/abc123");

        // Assert - retried (tombstone preserved), never discarded: acking would destroy the backend's cleanup
        // ledger while the blob survives.
        result.Should().Be(BlobDeleteResult.Retry);
        result.Should().NotBe(BlobDeleteResult.Discarded);
    }

    sealed class StubPayloadStore : PayloadStore
    {
        readonly Exception? deleteError;

        public StubPayloadStore(Exception? deleteError) => this.deleteError = deleteError;

        public override Task DeleteAsync(string token, CancellationToken cancellationToken) =>
            this.deleteError is null ? Task.CompletedTask : throw this.deleteError;

        public override Task<string> UploadAsync(string payLoad, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<string> DownloadAsync(string token, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override bool IsKnownPayloadToken(string value) => true;
    }
}
