// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Moq;
using Xunit;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests;

public class BlobPayloadStoreTests
{
    const string ContainerName = "payloads";

    static Mock<BlobContainerClient> CreateContainer(Mock<BlobClient> blob, string expectedBlobName)
    {
        Mock<BlobContainerClient> container = new();
        container.Setup(c => c.Name).Returns(ContainerName);
        container.Setup(c => c.GetBlobClient(expectedBlobName)).Returns(blob.Object);
        return container;
    }

    static Mock<BlobClient> CreateBlob(bool existed)
    {
        Mock<BlobClient> blob = new();
        blob
            .Setup(b => b.DeleteIfExistsAsync(
                It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(existed, Mock.Of<Response>()));
        return blob;
    }

    [Fact]
    public async Task DeleteAsync_ValidToken_DeletesBackingBlobIncludingSnapshots()
    {
        // Arrange
        Mock<BlobClient> blob = CreateBlob(existed: true);
        Mock<BlobContainerClient> container = CreateContainer(blob, "abc123");
        BlobPayloadStore store = new(container.Object, new LargePayloadStorageOptions());

        // Act
        await store.DeleteAsync($"blob:v1:{ContainerName}:abc123", CancellationToken.None);

        // Assert
        container.Verify(c => c.GetBlobClient("abc123"), Times.Once);
        blob.Verify(
            b => b.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_MissingBlob_IsIdempotentAndDoesNotThrow()
    {
        // Arrange
        Mock<BlobClient> blob = CreateBlob(existed: false);
        Mock<BlobContainerClient> container = CreateContainer(blob, "missing");
        BlobPayloadStore store = new(container.Object, new LargePayloadStorageOptions());

        // Act (a missing blob must be a no-op, not an error)
        await store.DeleteAsync($"blob:v1:{ContainerName}:missing", CancellationToken.None);

        // Assert
        blob.Verify(
            b => b.DeleteIfExistsAsync(
                It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ContainerMismatch_ThrowsAndDoesNotDelete()
    {
        // Arrange
        Mock<BlobClient> blob = CreateBlob(existed: true);
        Mock<BlobContainerClient> container = CreateContainer(blob, "abc123");
        BlobPayloadStore store = new(container.Object, new LargePayloadStorageOptions());

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
        BlobPayloadStore store = new(container.Object, new LargePayloadStorageOptions());

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteAsync(token, CancellationToken.None));
    }
}
