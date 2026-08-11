// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure;
using FluentAssertions;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Client;
using Xunit;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests.AutoPurge;

public class DeleteExternalBlobActivityTests
{
    const string V2Token = "blob:v2:https://acct.blob.core.windows.net/payloads/abc123";

    [Fact]
    public async Task RunAsync_WhenDeleteThrowsRequestFailed400_QuarantinesAsTokenNotPurgeable()
    {
        // Arrange - a Status 400 (e.g. InvalidResourceName) is a permanent service rejection.
        StubPayloadStore store = new(new RequestFailedException(400, "bad", "InvalidResourceName", null));
        DeleteExternalBlobActivity activity = new(store, new TestLogger<DeleteExternalBlobActivity>());

        // Act
        BlobPurgeOutcome outcome = await activity.RunAsync(null!, V2Token);

        // Assert - quarantined (evidence preserved), never a success-shaped discard. The storage error code
        // is what distinguishes this from the parse-failure cases, which share the same reason.
        outcome.Disposition.Should().Be(LargePayloadPurgeDisposition.Quarantined);
        outcome.Reason.Should().Be(LargePayloadPurgeReason.TokenNotPurgeable);
        outcome.StorageErrorCode.Should().Be("InvalidResourceName");
    }

    [Fact]
    public async Task RunAsync_WhenDeleteThrowsRequestFailedNon400_RetriesAsTransient()
    {
        // Arrange - a Status 503 that escaped the SDK's internal retries is still treated as transient.
        StubPayloadStore store = new(new RequestFailedException(503, "busy", "ServerBusy", null));
        DeleteExternalBlobActivity activity = new(store, new TestLogger<DeleteExternalBlobActivity>());

        // Act
        BlobPurgeOutcome outcome = await activity.RunAsync(null!, V2Token);

        // Assert
        outcome.Disposition.Should().Be(LargePayloadPurgeDisposition.Retry);
        outcome.Reason.Should().Be(LargePayloadPurgeReason.StorageFailure);
        outcome.StorageErrorCode.Should().Be("ServerBusy");
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task RunAsync_WhenDeleteThrowsAuthorizationFailure_Retries(int status)
    {
        // Arrange - authorization can be fixed by reconfiguration, so it stays recoverable.
        StubPayloadStore store = new(new RequestFailedException(status, "denied", "AuthorizationFailure", null));
        DeleteExternalBlobActivity activity = new(store, new TestLogger<DeleteExternalBlobActivity>());

        // Act
        BlobPurgeOutcome outcome = await activity.RunAsync(null!, V2Token);

        // Assert
        outcome.Disposition.Should().Be(LargePayloadPurgeDisposition.Retry);
        outcome.Reason.Should().Be(LargePayloadPurgeReason.StorageFailure);
    }

    [Fact]
    public async Task RunAsync_WhenDeleteThrowsPayloadStorageException_RetriesAsStorageFailure()
    {
        // Arrange - the payload lives in a storage account the configured credential cannot reach. That is
        // recoverable after a configuration or credential change, so it is deferred rather than discarded.
        StubPayloadStore store = new(new PayloadStorageException("cross-account delete requires identity auth"));
        DeleteExternalBlobActivity activity = new(store, new TestLogger<DeleteExternalBlobActivity>());

        // Act
        BlobPurgeOutcome outcome = await activity.RunAsync(
            null!, "blob:v2:https://other.blob.core.windows.net/c/abc123");

        // Assert
        outcome.Disposition.Should().Be(LargePayloadPurgeDisposition.Retry);
        outcome.Reason.Should().Be(LargePayloadPurgeReason.StorageFailure);
    }

    [Fact]
    public async Task RunAsync_V1Token_QuarantinesWithoutCallingStore()
    {
        // Arrange - a v1 token names a container but not the storage account, so a delete against the
        // configured account cannot be verified and would falsely report success if the store was repointed.
        Mock<PayloadStore> store = new();
        DeleteExternalBlobActivity activity = new(store.Object, new TestLogger<DeleteExternalBlobActivity>());

        // Act
        BlobPurgeOutcome outcome = await activity.RunAsync(null!, "blob:v1:payloads:abc123");

        // Assert - quarantined by the gate, and the store's DeleteAsync was never invoked.
        outcome.Disposition.Should().Be(LargePayloadPurgeDisposition.Quarantined);
        outcome.Reason.Should().Be(LargePayloadPurgeReason.TokenNotPurgeable);
        store.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_UnknownTokenVersion_RetriesWithoutCallingStore()
    {
        // Arrange - an unrecognized prefix most likely came from a newer SDK, which recovers after an upgrade.
        Mock<PayloadStore> store = new();
        DeleteExternalBlobActivity activity = new(store.Object, new TestLogger<DeleteExternalBlobActivity>());

        // Act
        BlobPurgeOutcome outcome = await activity.RunAsync(null!, "blob:v9:https://acct.blob.core.windows.net/c/x");

        // Assert - retried, NOT quarantined: quarantine is terminal and this can self-heal. This is the one
        // row where TokenNotPurgeable does not mean Quarantined, so the disposition is asserted deliberately:
        // deriving it from the reason would strand rows that an SDK upgrade would have resolved.
        outcome.Disposition.Should().Be(LargePayloadPurgeDisposition.Retry);
        outcome.Reason.Should().Be(LargePayloadPurgeReason.TokenNotPurgeable);
        store.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_MalformedV2Token_Quarantines()
    {
        // Arrange - a recognized v2 prefix whose body does not parse; the store signals that with
        // ArgumentException. Retrying can never fix a body the SDK itself produced malformed.
        StubPayloadStore store = new(new ArgumentException("Invalid token"));
        DeleteExternalBlobActivity activity = new(store, new TestLogger<DeleteExternalBlobActivity>());

        // Act
        BlobPurgeOutcome outcome = await activity.RunAsync(null!, "blob:v2:not-a-uri");

        // Assert - contrast with the unknown-prefix case above, which shares this reason but is retried.
        outcome.Disposition.Should().Be(LargePayloadPurgeDisposition.Quarantined);
        outcome.Reason.Should().Be(LargePayloadPurgeReason.TokenNotPurgeable);
    }

    [Fact]
    public async Task RunAsync_V2Token_CallsStoreAndReportsDeleted()
    {
        // Arrange - a self-describing v2 token is not gated and must reach the store.
        Mock<PayloadStore> store = new();
        store.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PayloadDeleteOutcome.Deleted);
        DeleteExternalBlobActivity activity = new(store.Object, new TestLogger<DeleteExternalBlobActivity>());

        // Act
        BlobPurgeOutcome outcome = await activity.RunAsync(null!, V2Token);

        // Assert
        outcome.Disposition.Should().Be(LargePayloadPurgeDisposition.Deleted);
        outcome.Reason.Should().Be(LargePayloadPurgeReason.BlobDeleted);
        store.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenBlobAlreadyAbsent_ReportsDeleted()
    {
        // Arrange - deletion is idempotent, so a blob a previous attempt already removed is not a failure.
        Mock<PayloadStore> store = new();
        store.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PayloadDeleteOutcome.AlreadyAbsent);
        DeleteExternalBlobActivity activity = new(store.Object, new TestLogger<DeleteExternalBlobActivity>());

        // Act
        BlobPurgeOutcome outcome = await activity.RunAsync(null!, V2Token);

        // Assert
        outcome.Disposition.Should().Be(LargePayloadPurgeDisposition.Deleted);
        outcome.Reason.Should().Be(LargePayloadPurgeReason.BlobAlreadyAbsent);
    }

    [Fact]
    public async Task RunAsync_WhenBlobNotStoreOwned_ResolvesTombstoneWithoutDeleting()
    {
        // Arrange - the blob exists but carries no ownership marker, so the store left it untouched.
        Mock<PayloadStore> store = new();
        store.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PayloadDeleteOutcome.NotStoreOwned);
        DeleteExternalBlobActivity activity = new(store.Object, new TestLogger<DeleteExternalBlobActivity>());

        // Act
        BlobPurgeOutcome outcome = await activity.RunAsync(null!, V2Token);

        // Assert - the tombstone is still resolved: a blob the store does not own is not the store's to delete,
        // and re-serving the row forever would never make it deletable.
        outcome.Disposition.Should().Be(LargePayloadPurgeDisposition.Deleted);
        outcome.Reason.Should().Be(LargePayloadPurgeReason.BlobNotStoreOwned);
    }

    [Fact]
    public async Task RunAsync_WhenStoreDoesNotSupportDelete_RetriesToPreserveTombstone()
    {
        // Arrange - a store that cannot delete (the base PayloadStore.DeleteAsync throws NotSupportedException).
        StubPayloadStore store = new(new NotSupportedException());
        DeleteExternalBlobActivity activity = new(store, new TestLogger<DeleteExternalBlobActivity>());

        // Act
        BlobPurgeOutcome outcome = await activity.RunAsync(null!, V2Token);

        // Assert - retried (tombstone preserved): resolving it would destroy the backend's cleanup ledger while
        // the blob survives.
        outcome.Disposition.Should().Be(LargePayloadPurgeDisposition.Retry);
        outcome.Reason.Should().Be(LargePayloadPurgeReason.StoreCannotDelete);
    }

    [Fact]
    public async Task RunAsync_WhenDeleteTimesOut_RetriesAsTransient()
    {
        // Arrange - a non-Azure exception (timeout / network failure) must not drop a blob on doubt.
        StubPayloadStore store = new(new TimeoutException());
        DeleteExternalBlobActivity activity = new(store, new TestLogger<DeleteExternalBlobActivity>());

        // Act
        BlobPurgeOutcome outcome = await activity.RunAsync(null!, V2Token);

        // Assert
        outcome.Disposition.Should().Be(LargePayloadPurgeDisposition.Retry);
        outcome.Reason.Should().Be(LargePayloadPurgeReason.StorageFailure);
        outcome.StorageErrorCode.Should().BeNull();
    }

    sealed class StubPayloadStore : PayloadStore
    {
        readonly Exception? deleteError;

        public StubPayloadStore(Exception? deleteError) => this.deleteError = deleteError;

        public override Task<PayloadDeleteOutcome> DeleteAsync(string token, CancellationToken cancellationToken) =>
            this.deleteError is null
                ? Task.FromResult(PayloadDeleteOutcome.Deleted)
                : throw this.deleteError;

        public override Task<string> UploadAsync(string payLoad, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<string> DownloadAsync(string token, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override bool IsKnownPayloadToken(string value) => true;
    }
}
