// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests;

/// <summary>
/// Unit tests for <see cref="BlobPayloadStore.DeleteAsync"/>, covering legacy v1 back-compatibility, the
/// self-describing v2 token resolution (same account, cross-account with identity, cross-account without), and
/// the ownership marker that gates every delete.
/// </summary>
public class BlobPayloadStoreDeleteTests
{
    const string ContainerName = "payloads";
    const string ConfiguredAccountUrl = "https://myaccount.blob.core.windows.net";

    static readonly ETag KnownETag = new("\"0x8DTEST\"");

    static Mock<BlobContainerClient> CreateContainer(Mock<BlobClient> blob, string expectedBlobName)
    {
        Mock<BlobContainerClient> container = new();
        container.Setup(c => c.Name).Returns(ContainerName);
        container.Setup(c => c.Uri).Returns(new Uri($"{ConfiguredAccountUrl}/{ContainerName}"));
        container.Setup(c => c.GetBlobClient(expectedBlobName)).Returns(blob.Object);
        return container;
    }

    /// <summary>
    /// Creates a blob that exists and carries this store's ownership marker, which is the ordinary case for a
    /// payload the store itself uploaded.
    /// </summary>
    static Mock<BlobClient> CreateBlob(bool existed) => CreateBlob(existed, owned: true);

    static Mock<BlobClient> CreateBlob(bool existed, bool owned)
    {
        Mock<BlobClient> blob = new();

        if (existed)
        {
            Dictionary<string, string> metadata = owned
                ? new() { [BlobPayloadStore.OwnershipMarkerName] = BlobPayloadStore.OwnershipMarkerValue }
                : new() { ["customer-tag"] = "not-ours" };

            blob
                .Setup(b => b.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(
                    BlobsModelFactory.BlobProperties(metadata: metadata, eTag: KnownETag), Mock.Of<Response>()));
        }
        else
        {
            blob
                .Setup(b => b.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(404, "not found", "BlobNotFound", null));
        }

        blob
            .Setup(b => b.DeleteIfExistsAsync(
                It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(existed, Mock.Of<Response>()));
        return blob;
    }

    [Fact]
    public async Task DeleteAsync_V1Token_DeletesBackingBlobIncludingSnapshots()
    {
        // Arrange
        Mock<BlobClient> blob = CreateBlob(existed: true);
        Mock<BlobContainerClient> container = CreateContainer(blob, "abc123");
        BlobPayloadStore store = new(new LargePayloadStorageOptions(), container.Object);

        // Act
        PayloadDeleteOutcome outcome = await store.DeleteAsync(
            $"blob:v1:{ContainerName}:abc123", CancellationToken.None);

        // Assert
        outcome.Should().Be(PayloadDeleteOutcome.Deleted);
        container.Verify(c => c.GetBlobClient("abc123"), Times.Once);
        blob.Verify(
            b => b.DeleteIfExistsAsync(
                DeleteSnapshotsOption.IncludeSnapshots,
                It.Is<BlobRequestConditions>(c => c.IfMatch == KnownETag),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_MissingBlob_IsIdempotentAndReportsAlreadyAbsent()
    {
        // Arrange
        Mock<BlobClient> blob = CreateBlob(existed: false);
        Mock<BlobContainerClient> container = CreateContainer(blob, "missing");
        BlobPayloadStore store = new(new LargePayloadStorageOptions(), container.Object);

        // Act (a missing blob must be a no-op, not an error)
        PayloadDeleteOutcome outcome = await store.DeleteAsync(
            $"blob:v1:{ContainerName}:missing", CancellationToken.None);

        // Assert - the ownership probe already proved absence, so no delete request is needed.
        outcome.Should().Be(PayloadDeleteOutcome.AlreadyAbsent);
        blob.Verify(
            b => b.DeleteIfExistsAsync(
                It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_BlobWithoutOwnershipMarker_LeavesBlobUntouched()
    {
        // Arrange - a blob whose token text matches the v2 grammar but which this store never wrote (for
        // example a customer dataset referenced by URL, or a payload written before the marker shipped).
        Mock<BlobClient> blob = CreateBlob(existed: true, owned: false);
        Mock<BlobContainerClient> container = CreateContainer(blob, "abc123");
        BlobPayloadStore store = new(new LargePayloadStorageOptions(), container.Object);

        // Act
        PayloadDeleteOutcome outcome = await store.DeleteAsync(
            $"blob:v2:{ConfiguredAccountUrl}/{ContainerName}/abc123", CancellationToken.None);

        // Assert - reported distinctly and, critically, never deleted.
        outcome.Should().Be(PayloadDeleteOutcome.NotStoreOwned);
        blob.Verify(
            b => b.DeleteIfExistsAsync(
                It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_V1TokenContainerMismatch_ThrowsAndDoesNotDelete()
    {
        // Arrange - a v1 token does not carry the account, so its container must match the configured store.
        Mock<BlobClient> blob = CreateBlob(existed: true);
        Mock<BlobContainerClient> container = CreateContainer(blob, "abc123");
        BlobPayloadStore store = new(new LargePayloadStorageOptions(), container.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.DeleteAsync("blob:v1:other-container:abc123", CancellationToken.None));
        blob.Verify(
            b => b.DeleteIfExistsAsync(
                It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("blob:v1:only-container")]
    [InlineData("blob:v1::blobname")]
    public async Task DeleteAsync_InvalidToken_ThrowsArgumentException(string token)
    {
        // Arrange
        Mock<BlobClient> blob = CreateBlob(existed: true);
        Mock<BlobContainerClient> container = CreateContainer(blob, "abc123");
        BlobPayloadStore store = new(new LargePayloadStorageOptions(), container.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteAsync(token, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_V2TokenSameContainer_DeletesViaConfiguredClient()
    {
        // Arrange - a self-describing v2 token whose account+container match the configured store. The store
        // recognizes it via IsConfiguredContainer and deletes through the existing container client (which works
        // with any auth mode), never building a cross-account client.
        Mock<BlobClient> blob = CreateBlob(existed: true);
        Mock<BlobContainerClient> container = CreateContainer(blob, "abc123");
        BlobPayloadStore store = new(new LargePayloadStorageOptions(), container.Object);

        // Act
        PayloadDeleteOutcome outcome = await store.DeleteAsync(
            $"blob:v2:{ConfiguredAccountUrl}/{ContainerName}/abc123", CancellationToken.None);

        // Assert
        outcome.Should().Be(PayloadDeleteOutcome.Deleted);
        container.Verify(c => c.GetBlobClient("abc123"), Times.Once);
        blob.Verify(
            b => b.DeleteIfExistsAsync(
                DeleteSnapshotsOption.IncludeSnapshots,
                It.Is<BlobRequestConditions>(c => c.IfMatch == KnownETag),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_V2TokenDifferentAccountWithCredential_DoesNotUseConfiguredContainer()
    {
        // Arrange - a v2 token pointing at a DIFFERENT account than the configured store, with identity auth
        // available. The store must build a BlobClient bound to the token's own account using the credential and
        // must not touch the configured container client. A credential that throws on token acquisition proves
        // the cross-account path is taken without any network call (the throw short-circuits before the send).
        Mock<BlobClient> blob = CreateBlob(existed: true);
        Mock<BlobContainerClient> container = CreateContainer(blob, "abc123");
        SentinelCredential credential = new();
        BlobPayloadStore store = new(
            new LargePayloadStorageOptions(new Uri(ConfiguredAccountUrl), credential), container.Object);
        string token = "blob:v2:https://otheraccount.blob.core.windows.net/othercontainer/abc123";

        // Act
        Exception error = await Assert.ThrowsAnyAsync<Exception>(
            () => store.DeleteAsync(token, CancellationToken.None));

        // Assert - the cross-account BlobClient invoked our sentinel credential (directly or wrapped), proving
        // that branch ran; the configured container client is never used for a different account.
        Assert.True(
            error is SentinelCredential.InvokedException || error.InnerException is SentinelCredential.InvokedException,
            $"Expected the cross-account BlobClient to invoke the credential, but got: {error}");
        container.Verify(c => c.GetBlobClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_V2TokenDifferentAccountWithoutCredential_ThrowsPayloadStorageExceptionAndDoesNotDelete()
    {
        // Arrange - the configured store uses a connection string (account-key auth, no TokenCredential) and the
        // token points at a different account. Account keys are account-specific, so the delete cannot cross
        // accounts and must fail fast with a clear PayloadStorageException before any network call.
        Mock<BlobClient> blob = CreateBlob(existed: true);
        Mock<BlobContainerClient> container = CreateContainer(blob, "abc123");
        BlobPayloadStore store = new(new LargePayloadStorageOptions("UseDevelopmentStorage=true"), container.Object);
        string token = "blob:v2:https://otheraccount.blob.core.windows.net/othercontainer/abc123";

        // Act
        PayloadStorageException error = await Assert.ThrowsAsync<PayloadStorageException>(
            () => store.DeleteAsync(token, CancellationToken.None));

        // Assert - fails before touching the network or the configured container.
        Assert.Contains("different storage account", error.Message, StringComparison.Ordinal);
        container.Verify(c => c.GetBlobClient(It.IsAny<string>()), Times.Never);
        blob.Verify(
            b => b.DeleteIfExistsAsync(
                It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // A TokenCredential that throws as soon as a token is requested, proving the cross-account BlobClient path
    // was taken without performing any network I/O.
    sealed class SentinelCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new InvokedException();

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new InvokedException();

        public sealed class InvokedException : Exception
        {
        }
    }
}
