// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure;
using FluentAssertions;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Client;
using Xunit;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests.AutoPurge;

/// <summary>
/// The reported outcome carries the disposition alone, so each case pins two things: the disposition, which is
/// what the backend acts on, and the logged cause, which is now the only record of why an attempt reached that
/// disposition. Several branches share a disposition and are told apart only by their cause, so asserting the
/// disposition alone would let two branches collapse into one unnoticed.
/// </summary>
public class DeleteExternalBlobActivityTests
{
    const string V2Token = "blob:v2:https://acct.blob.core.windows.net/payloads/abc123";

    [Fact]
    public async Task RunAsync_WhenDeleteThrowsRequestFailed400_Quarantines()
    {
        // Arrange - a Status 400 (e.g. InvalidResourceName) is a permanent service rejection.
        StubPayloadStore store = new(new RequestFailedException(400, "bad", "InvalidResourceName", null));
        TestLogger<DeleteExternalBlobActivity> logger = new();
        DeleteExternalBlobActivity activity = new(store, logger);

        // Act
        BlobPurgeOutcome outcome = await activity.RunAsync(null!, V2Token);

        // Assert - quarantined (evidence preserved), never a success-shaped discard. The sanitized storage
        // error code is logged alongside the cause and is what distinguishes this from the parse failures.
        outcome.Disposition.Should().Be(LargePayloadPurgeDisposition.Quarantined);
        logger.Logs.Should().ContainSingle(
            l => l.Message.Contains("InvalidStorageRequest") && l.Message.Contains("InvalidResourceName"));
    }

    [Fact]
    public async Task RunAsync_WhenDeleteThrowsRequestFailedNon400_RetriesAsTransient()
    {
        // Arrange - a Status 503 that escaped the SDK's internal retries is still treated as transient.
        StubPayloadStore store = new(new RequestFailedException(503, "busy", "ServerBusy", null));
        TestLogger<DeleteExternalBlobActivity> logger = new();
        DeleteExternalBlobActivity activity = new(store, logger);

        // Act
        BlobPurgeOutcome outcome = await activity.RunAsync(null!, V2Token);

        // Assert
        outcome.Disposition.Should().Be(LargePayloadPurgeDisposition.Retry);
        logger.Logs.Should().ContainSingle(
            l => l.Message.Contains("TransientStorageFailure") && l.Message.Contains("ServerBusy"));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task RunAsync_WhenDeleteThrowsAuthorizationFailure_Retries(int status)
    {
        // Arrange - authorization can be fixed by reconfiguration, so it stays recoverable.
        StubPayloadStore store = new(new RequestFailedException(status, "denied", "AuthorizationFailure", null));
        TestLogger<DeleteExternalBlobActivity> logger = new();
        DeleteExternalBlobActivity activity = new(store, logger);

        // Act
        BlobPurgeOutcome outcome = await activity.RunAsync(null!, V2Token);

        // Assert
        outcome.Disposition.Should().Be(LargePayloadPurgeDisposition.Retry);
        logger.Logs.Should().ContainSingle(l => l.Message.Contains("StorageAuthorizationFailed"));
    }

    [Fact]
    public async Task RunAsync_WhenDeleteThrowsPayloadStorageException_Retries()
    {
        // Arrange - the payload lives in a storage account the configured credential cannot reach. That is
        // recoverable after a configuration or credential change, so it is deferred rather than discarded.
        StubPayloadStore store = new(new PayloadStorageException("cross-account delete requires identity auth"));
        TestLogger<DeleteExternalBlobActivity> logger = new();
        DeleteExternalBlobActivity activity = new(store, logger);

        // Act
        BlobPurgeOutcome outcome = await activity.RunAsync(
            null!, "blob:v2:https://other.blob.core.windows.net/c/abc123");

        // Assert - the cause is what separates this from the other retryable storage failures, which the
        // contract no longer distinguishes.
        outcome.Disposition.Should().Be(LargePayloadPurgeDisposition.Retry);
        logger.Logs.Should().ContainSingle(l => l.Message.Contains("StorageAccountUnreachable"));
    }

    [Fact]
    public async Task RunAsync_V1Token_QuarantinesWithoutCallingStore()
    {
        // Arrange - a v1 token names a container but not the storage account, so a delete against the
        // configured account cannot be verified and would falsely report success if the store was repointed.
        Mock<PayloadStore> store = new();
        TestLogger<DeleteExternalBlobActivity> logger = new();
        DeleteExternalBlobActivity activity = new(store.Object, logger);

        // Act
        BlobPurgeOutcome outcome = await activity.RunAsync(null!, "blob:v1:payloads:abc123");

        // Assert - quarantined by the gate, and the store's DeleteAsync was never invoked.
        outcome.Disposition.Should().Be(LargePayloadPurgeDisposition.Quarantined);
        logger.Logs.Should().ContainSingle(l => l.Message.Contains("LegacyV1Token"));
        store.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_UnknownTokenVersion_RetriesWithoutCallingStore()
    {
        // Arrange - an unrecognized prefix most likely came from a newer SDK, which recovers after an upgrade.
        Mock<PayloadStore> store = new();
        TestLogger<DeleteExternalBlobActivity> logger = new();
        DeleteExternalBlobActivity activity = new(store.Object, logger);

        // Act
        BlobPurgeOutcome outcome = await activity.RunAsync(null!, "blob:v9:https://acct.blob.core.windows.net/c/x");

        // Assert - retried, NOT quarantined: quarantine is terminal and requires an operator to unwind, while a
        // deferral only leaves the row idle and visible. This branch and the token branches that quarantine are
        // told apart only by cause, so the disposition is asserted deliberately: folding them together would
        // strand rows that an SDK upgrade would have resolved.
        outcome.Disposition.Should().Be(LargePayloadPurgeDisposition.Retry);
        logger.Logs.Should().ContainSingle(l => l.Message.Contains("UnsupportedTokenVersion"));
        store.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_MalformedV2Token_Quarantines()
    {
        // Arrange - a recognized v2 prefix whose body does not parse; the store signals that with
        // ArgumentException. Retrying can never fix a body the SDK itself produced malformed.
        StubPayloadStore store = new(new ArgumentException("Invalid token"));
        TestLogger<DeleteExternalBlobActivity> logger = new();
        DeleteExternalBlobActivity activity = new(store, logger);

        // Act
        BlobPurgeOutcome outcome = await activity.RunAsync(null!, "blob:v2:not-a-uri");

        // Assert - contrast with the unknown-prefix case above, which is retried.
        outcome.Disposition.Should().Be(LargePayloadPurgeDisposition.Quarantined);
        logger.Logs.Should().ContainSingle(l => l.Message.Contains("MalformedToken"));
    }

    [Fact]
    public async Task RunAsync_V2Token_CallsStoreAndReportsDeleted()
    {
        // Arrange - a self-describing v2 token is not gated and must reach the store.
        Mock<PayloadStore> store = new();
        store.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PayloadDeleteOutcome.Deleted);
        TestLogger<DeleteExternalBlobActivity> logger = new();
        DeleteExternalBlobActivity activity = new(store.Object, logger);

        // Act
        BlobPurgeOutcome outcome = await activity.RunAsync(null!, V2Token);

        // Assert - an ordinary success is silent; any log here would mean a failure branch was taken.
        outcome.Disposition.Should().Be(LargePayloadPurgeDisposition.Deleted);
        logger.Logs.Should().BeEmpty();
        store.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenBlobAlreadyAbsent_ReportsDeleted()
    {
        // Arrange - deletion is idempotent, so a blob a previous attempt already removed is not a failure.
        Mock<PayloadStore> store = new();
        store.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PayloadDeleteOutcome.AlreadyAbsent);
        TestLogger<DeleteExternalBlobActivity> logger = new();
        DeleteExternalBlobActivity activity = new(store.Object, logger);

        // Act
        BlobPurgeOutcome outcome = await activity.RunAsync(null!, V2Token);

        // Assert
        outcome.Disposition.Should().Be(LargePayloadPurgeDisposition.Deleted);
        logger.Logs.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_WhenBlobNotStoreOwned_ResolvesTombstoneWithoutDeleting()
    {
        // Arrange - the blob exists but carries no ownership marker, so the store left it untouched.
        Mock<PayloadStore> store = new();
        store.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PayloadDeleteOutcome.NotStoreOwned);
        TestLogger<DeleteExternalBlobActivity> logger = new();
        DeleteExternalBlobActivity activity = new(store.Object, logger);

        // Act
        BlobPurgeOutcome outcome = await activity.RunAsync(null!, V2Token);

        // Assert - the tombstone is still resolved: a blob the store does not own is not the store's to delete,
        // and re-serving the row forever would never make it deletable. The reported result is now identical to
        // an ordinary delete, so the log is the only thing that records the difference.
        outcome.Disposition.Should().Be(LargePayloadPurgeDisposition.Deleted);
        logger.Logs.Should().ContainSingle(l => l.Message.Contains("ownership marker"));
    }

    [Fact]
    public async Task RunAsync_WhenStoreDoesNotSupportDelete_RetriesToPreserveTombstone()
    {
        // Arrange - a store that cannot delete (the base PayloadStore.DeleteAsync throws NotSupportedException).
        StubPayloadStore store = new(new NotSupportedException());
        TestLogger<DeleteExternalBlobActivity> logger = new();
        DeleteExternalBlobActivity activity = new(store, logger);

        // Act
        BlobPurgeOutcome outcome = await activity.RunAsync(null!, V2Token);

        // Assert - retried (tombstone preserved): resolving it would destroy the backend's cleanup ledger while
        // the blob survives.
        outcome.Disposition.Should().Be(LargePayloadPurgeDisposition.Retry);
        logger.Logs.Should().ContainSingle(l => l.Message.Contains("StoreCannotDelete"));
    }

    [Fact]
    public async Task RunAsync_WhenDeleteTimesOut_RetriesAsTransient()
    {
        // Arrange - a non-Azure exception (timeout / network failure) must not drop a blob on doubt.
        StubPayloadStore store = new(new TimeoutException());
        TestLogger<DeleteExternalBlobActivity> logger = new();
        DeleteExternalBlobActivity activity = new(store, logger);

        // Act
        BlobPurgeOutcome outcome = await activity.RunAsync(null!, V2Token);

        // Assert - storage reported no code here, so the exception's type name is what identifies the failure.
        outcome.Disposition.Should().Be(LargePayloadPurgeDisposition.Retry);
        logger.Logs.Should().ContainSingle(l => l.Message.Contains("UnexpectedFailure:TimeoutException"));
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
