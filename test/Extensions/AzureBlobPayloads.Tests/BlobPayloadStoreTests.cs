// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.DurableTask;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests;

/// <summary>
/// Unit tests for <see cref="BlobPayloadStore"/>'s container-initialization caching behavior
/// (see https://github.com/microsoft/durabletask-dotnet/issues/771).
/// </summary>
public class BlobPayloadStoreTests
{
    [Fact]
    public async Task UploadAsync_ConcurrentCallers_OnlyCreatesContainerOnce()
    {
        // Arrange
        int createCalls = 0;
        Mock<BlobContainerClient> containerClientMock = CreateContainerClientMock();
        containerClientMock
            .Setup(c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                Interlocked.Increment(ref createCalls);
                await Task.Delay(50);
                return (Response<BlobContainerInfo>)null!;
            });

        BlobPayloadStore store = new(new LargePayloadStorageOptions(), containerClientMock.Object);

        // Act: fire many concurrent uploads against a fresh (uninitialized) store.
        Task<string>[] uploads = Enumerable.Range(0, 10)
            .Select(_ => store.UploadAsync("payload", CancellationToken.None))
            .ToArray();
        await Task.WhenAll(uploads);

        // Assert
        createCalls.Should().Be(1);
        containerClientMock.Verify(
            c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadAsync_FailedInitialization_IsRetriedOnNextCall()
    {
        // Arrange: the first CreateIfNotExistsAsync attempt fails (e.g. transient storage error),
        // the second succeeds.
        Mock<BlobContainerClient> containerClientMock = CreateContainerClientMock();
        containerClientMock
            .SetupSequence(c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(503, "Service unavailable"))
            .ReturnsAsync((Response<BlobContainerInfo>)null!);

        BlobPayloadStore store = new(new LargePayloadStorageOptions(), containerClientMock.Object);

        // Act
        Func<Task> firstUpload = () => store.UploadAsync("payload", CancellationToken.None);
        string secondToken;
        await firstUpload.Should().ThrowAsync<RequestFailedException>();
        secondToken = await store.UploadAsync("payload", CancellationToken.None);

        // Assert: the failed attempt was not cached, so the second call retried initialization.
        secondToken.Should().NotBeNullOrEmpty();
        containerClientMock.Verify(
            c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task UploadAsync_CancelledCaller_DoesNotFaultSharedInitializationForOtherCallers()
    {
        // Arrange: control exactly when the shared initialization completes.
        TaskCompletionSource<Response<BlobContainerInfo>> initTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<BlobContainerClient> containerClientMock = CreateContainerClientMock();
        containerClientMock
            .Setup(c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(initTcs.Task);

        BlobPayloadStore store = new(new LargePayloadStorageOptions(), containerClientMock.Object);

        using CancellationTokenSource cts = new();
        Task<string> cancelledUpload = store.UploadAsync("payload", cts.Token);
        Task<string> otherUpload = store.UploadAsync("payload", CancellationToken.None);

        // Act: cancel the first caller while initialization is still in flight, then let
        // initialization complete successfully for everyone else.
        cts.Cancel();
        await Task.Delay(50);
        initTcs.SetResult(null!);

        // Assert
        Func<Task> awaitCancelled = () => cancelledUpload;
        await awaitCancelled.Should().ThrowAsync<OperationCanceledException>();

        string token = await otherUpload;
        token.Should().NotBeNullOrEmpty();
        containerClientMock.Verify(
            c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadAsync_ContainerDeletedAfterInitialization_ResetsCacheAndRecreatesContainer()
    {
        // Arrange: initialization always succeeds (from the SDK's point of view), but the first
        // blob write fails because the container was deleted out-of-band after initialization.
        Mock<BlobContainerClient> containerClientMock = CreateContainerClientMock();
        containerClientMock
            .Setup(c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Response<BlobContainerInfo>)null!);

        Mock<BlobClient> blobClientMock = new();
        blobClientMock
            .SetupSequence(b => b.OpenWriteAsync(It.IsAny<bool>(), It.IsAny<BlobOpenWriteOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "The specified container does not exist.", BlobErrorCode.ContainerNotFound.ToString(), null))
            .ReturnsAsync(new MemoryStream());
        containerClientMock.Setup(c => c.GetBlobClient(It.IsAny<string>())).Returns(blobClientMock.Object);

        BlobPayloadStore store = new(new LargePayloadStorageOptions(), containerClientMock.Object);

        // Act
        Func<Task> firstUpload = () => store.UploadAsync("payload", CancellationToken.None);
        await firstUpload.Should().ThrowAsync<RequestFailedException>();

        string secondToken = await store.UploadAsync("payload", CancellationToken.None);

        // Assert: the container-not-found failure reset the cache, so the second upload
        // recreated the container instead of assuming it still existed.
        secondToken.Should().NotBeNullOrEmpty();
        containerClientMock.Verify(
            c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task UploadAsync_UnrelatedStorageError_PropagatesWithoutInvalidatingCache()
    {
        // Arrange: initialization succeeds; the first blob write fails with an error that has
        // nothing to do with the container being missing (should not trigger re-initialization).
        Mock<BlobContainerClient> containerClientMock = CreateContainerClientMock();
        containerClientMock
            .Setup(c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Response<BlobContainerInfo>)null!);

        Mock<BlobClient> blobClientMock = new();
        blobClientMock
            .SetupSequence(b => b.OpenWriteAsync(It.IsAny<bool>(), It.IsAny<BlobOpenWriteOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(500, "Internal server error"))
            .ReturnsAsync(new MemoryStream());
        containerClientMock.Setup(c => c.GetBlobClient(It.IsAny<string>())).Returns(blobClientMock.Object);

        BlobPayloadStore store = new(new LargePayloadStorageOptions(), containerClientMock.Object);

        // Act
        Func<Task> firstUpload = () => store.UploadAsync("payload", CancellationToken.None);
        await firstUpload.Should().ThrowAsync<RequestFailedException>();

        string secondToken = await store.UploadAsync("payload", CancellationToken.None);

        // Assert: the unrelated error propagated to the caller, and initialization was not
        // repeated since the cached container state was still considered valid.
        secondToken.Should().NotBeNullOrEmpty();
        containerClientMock.Verify(
            c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    static Mock<BlobContainerClient> CreateContainerClientMock()
    {
        Mock<BlobContainerClient> containerClientMock = new();
        containerClientMock.Setup(c => c.Name).Returns("test-container");
        containerClientMock
            .Setup(c => c.GetBlobClient(It.IsAny<string>()))
            .Returns(() =>
            {
                Mock<BlobClient> blobClientMock = new();
                blobClientMock
                    .Setup(b => b.OpenWriteAsync(It.IsAny<bool>(), It.IsAny<BlobOpenWriteOptions>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(() => new MemoryStream());
                return blobClientMock.Object;
            });
        return containerClientMock;
    }
}
