// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

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
        const int WorkerCount = 16;
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

        // Act: release all workers from an async gate after each has reached it. The delayed
        // create keeps initialization in flight while the workers contend for the gate.
        TaskCompletionSource<bool> startGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int readyWorkers = 0;
        Task<string>[] uploads = Enumerable.Range(0, WorkerCount)
            .Select(_ => Task.Run(async () =>
            {
                if (Interlocked.Increment(ref readyWorkers) == WorkerCount)
                {
                    startGate.SetResult(true);
                }

                await startGate.Task;
                return await store.UploadAsync("payload", CancellationToken.None);
            }))
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
    public async Task UploadAsync_CancelledInitializer_AllowsWaitingCallerToRetry()
    {
        // Arrange: the first caller owns initialization until its token is cancelled. A waiting
        // caller must then acquire the gate and retry with its own uncancelled token.
        TaskCompletionSource<bool> firstCreateStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int createCalls = 0;
        Mock<BlobContainerClient> containerClientMock = CreateContainerClientMock();
        containerClientMock
            .Setup(c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns((
                PublicAccessType _,
                IDictionary<string, string> _,
                BlobContainerEncryptionScopeOptions _,
                CancellationToken cancellationToken) => CreateContainerAsync(cancellationToken));

        BlobPayloadStore store = new(new LargePayloadStorageOptions(), containerClientMock.Object);

        using CancellationTokenSource cts = new();
        Task<string> cancelledUpload = store.UploadAsync("payload", cts.Token);
        await firstCreateStarted.Task;
        Task<string> otherUpload = store.UploadAsync("payload", CancellationToken.None);

        // Act
        cts.Cancel();

        // Assert
        Task completedTask = await Task.WhenAny(cancelledUpload, Task.Delay(TimeSpan.FromSeconds(5)));
        completedTask.Should().BeSameAs(cancelledUpload, "the caller token should cancel the storage initialization");
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
            Times.Exactly(2));

        async Task<Response<BlobContainerInfo>> CreateContainerAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref createCalls) == 1)
            {
                firstCreateStarted.SetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return null!;
        }
    }

    [Fact]
    public async Task UploadAsync_CancelledWaiter_DoesNotCancelInitializer()
    {
        // Arrange: hold the first caller in initialization while a second caller waits for the
        // gate with an independently cancellable token.
        TaskCompletionSource<Response<BlobContainerInfo>> initTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> createStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<BlobContainerClient> containerClientMock = CreateContainerClientMock();
        containerClientMock
            .Setup(c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                createStarted.SetResult(true);
                return initTcs.Task;
            });

        BlobPayloadStore store = new(new LargePayloadStorageOptions(), containerClientMock.Object);
        Task<string> initializingUpload = store.UploadAsync("payload", CancellationToken.None);
        await createStarted.Task;

        using CancellationTokenSource cts = new();
        Task<string> waitingUpload = store.UploadAsync("payload", cts.Token);

        // Act
        cts.Cancel();

        // Assert
        Task completedTask = await Task.WhenAny(waitingUpload, Task.Delay(TimeSpan.FromSeconds(5)));
        completedTask.Should().BeSameAs(waitingUpload, "the caller token should cancel the wait for initialization");
        Func<Task> awaitWaitingUpload = () => waitingUpload;
        await awaitWaitingUpload.Should().ThrowAsync<OperationCanceledException>();

        initTcs.SetResult(null!);
        string token = await initializingUpload;
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
    public async Task UploadAsync_ContainerDeletedAfterInitializationAndDeletionSettles_RecreatesContainer()
    {
        // Arrange: the first write observes an out-of-band deletion, and the mocked re-create
        // succeeds as it would after Azure has finished deleting the container. While deletion is
        // still in progress, Azure can return ContainerBeingDeleted and that error propagates.
        Mock<BlobContainerClient> containerClientMock = CreateContainerClientMock();
        containerClientMock
            .Setup(c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Response<BlobContainerInfo>)null!);

        Mock<BlobClient> blobClientMock = new();
        blobClientMock.SetupGet(b => b.Uri).Returns(new Uri("https://testaccount.blob.core.windows.net/test-container/payload"));
        using MemoryStream successfulWriteStream = new();
        blobClientMock
            .SetupSequence(b => b.OpenWriteAsync(It.IsAny<bool>(), It.IsAny<BlobOpenWriteOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "The specified container does not exist.", BlobErrorCode.ContainerNotFound.ToString(), null))
            .ReturnsAsync(successfulWriteStream);
        containerClientMock.Setup(c => c.GetBlobClient(It.IsAny<string>())).Returns(blobClientMock.Object);

        BlobPayloadStore store = new(new LargePayloadStorageOptions(), containerClientMock.Object);

        // Act
        string token = await store.UploadAsync("payload", CancellationToken.None);

        // Assert: the missing-container failure reset the cached generation and this same upload
        // re-created the container after deletion had settled.
        token.Should().NotBeNullOrEmpty();
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
        blobClientMock.SetupGet(b => b.Uri).Returns(new Uri("https://testaccount.blob.core.windows.net/test-container/payload"));
        using MemoryStream retryWriteStream = new();
        blobClientMock
            .SetupSequence(b => b.OpenWriteAsync(It.IsAny<bool>(), It.IsAny<BlobOpenWriteOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(500, "Internal server error"))
            .ReturnsAsync(retryWriteStream);
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

    [Fact]
    public async Task UploadAsync_StaleContainerNotFoundFailure_DoesNotOverwriteNewerInitializer()
    {
        // Arrange: container creation always succeeds when invoked.
        int createCalls = 0;
        Mock<BlobContainerClient> containerClientMock = new();
        containerClientMock.Setup(c => c.Name).Returns("test-container");
        containerClientMock
            .Setup(c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Interlocked.Increment(ref createCalls);
                return Task.FromResult((Response<BlobContainerInfo>)null!);
            });

        // The first upload's blob write is held open (via this TCS) until the test explicitly
        // releases it, simulating a slow write that only discovers the container is gone after
        // other, faster uploads have already observed the deletion and re-initialized.
        TaskCompletionSource<Stream> firstUploadWriteTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        int getBlobClientCalls = 0;
        containerClientMock
            .Setup(c => c.GetBlobClient(It.IsAny<string>()))
            .Returns(() =>
            {
                int callIndex = Interlocked.Increment(ref getBlobClientCalls);
                Mock<BlobClient> blobClientMock = new();
                blobClientMock.SetupGet(b => b.Uri).Returns(new Uri($"https://testaccount.blob.core.windows.net/test-container/payload-{callIndex}"));
                switch (callIndex)
                {
                    case 1:
                        // First upload: its write stays pending until the test releases it.
                        blobClientMock
                            .Setup(b => b.OpenWriteAsync(It.IsAny<bool>(), It.IsAny<BlobOpenWriteOptions>(), It.IsAny<CancellationToken>()))
                            .Returns(firstUploadWriteTcs.Task);
                        break;
                    case 2:
                        // Second upload: discovers the container is gone immediately.
                        blobClientMock
                            .Setup(b => b.OpenWriteAsync(It.IsAny<bool>(), It.IsAny<BlobOpenWriteOptions>(), It.IsAny<CancellationToken>()))
                            .ThrowsAsync(new RequestFailedException(404, "The specified container does not exist.", BlobErrorCode.ContainerNotFound.ToString(), null));
                        break;
                    default:
                        blobClientMock
                            .Setup(b => b.OpenWriteAsync(It.IsAny<bool>(), It.IsAny<BlobOpenWriteOptions>(), It.IsAny<CancellationToken>()))
                            .ReturnsAsync(() => new MemoryStream());
                        break;
                }

                return blobClientMock.Object;
            });

        BlobPayloadStore store = new(new LargePayloadStorageOptions(), containerClientMock.Object);

        // Act
        // Upload 1 initializes the container (create call #1) and then blocks mid-write.
        Task<string> upload1 = store.UploadAsync("payload", CancellationToken.None);

        // Upload 2 reuses the already-cached initializer (no new create call), fails on write
        // with ContainerNotFound, and invalidates that initializer.
        Func<Task> upload2 = () => store.UploadAsync("payload", CancellationToken.None);
        await upload2.Should().ThrowAsync<RequestFailedException>();

        // Upload 3 observes the invalidated cache and re-initializes the container (create call
        // #2), publishing a newer initializer.
        string upload3Token = await store.UploadAsync("payload", CancellationToken.None);
        upload3Token.Should().NotBeNullOrEmpty();

        // Now let upload 1's stale write finally fail with the same ContainerNotFound error. Its
        // cache invalidation must not clobber the newer initializer upload 3 already published.
        firstUploadWriteTcs.SetException(new RequestFailedException(404, "The specified container does not exist.", BlobErrorCode.ContainerNotFound.ToString(), null));
        Func<Task> awaitUpload1 = () => upload1;
        await awaitUpload1.Should().ThrowAsync<RequestFailedException>();

        // Assert: a further upload reuses upload 3's initializer instead of triggering a third,
        // unnecessary re-initialization caused by upload 1's stale failure.
        string finalToken = await store.UploadAsync("payload", CancellationToken.None);
        finalToken.Should().NotBeNullOrEmpty();
        createCalls.Should().Be(2);
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
                blobClientMock.SetupGet(b => b.Uri).Returns(new Uri("https://testaccount.blob.core.windows.net/test-container/payload"));
                blobClientMock
                    .Setup(b => b.OpenWriteAsync(It.IsAny<bool>(), It.IsAny<BlobOpenWriteOptions>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(() => new MemoryStream());
                return blobClientMock.Object;
            });
        return containerClientMock;
    }
}
