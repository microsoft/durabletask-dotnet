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

        // Act: force genuinely concurrent entry into the single-flight logic. Each worker runs
        // on its own thread-pool thread (via Task.Run) and blocks at a Barrier until every
        // worker has arrived, so they all call UploadAsync at (as close to) the same instant as
        // possible. A simple sequential LINQ + Task.WhenAll loop would not exercise this: the
        // synchronous prefix of each call (up to the first real await) would run one after
        // another on the calling thread, letting the first caller publish the cached
        // initializer before any other caller's code even starts, which trivially "passes" even
        // a buggy, non-single-flight implementation.
        using Barrier barrier = new(WorkerCount);
        Task<string>[] uploads = Enumerable.Range(0, WorkerCount)
            .Select(_ => Task.Run(async () =>
            {
                barrier.SignalAndWait();
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

    [Fact]
    public async Task UploadAsync_AllWaitersCancelBeforeInitializationFails_NextUploadRetriesWithFreshAttempt()
    {
        // Arrange: control exactly when the shared initialization completes/fails. The first
        // invocation returns this controlled (eventually-faulted) task; any subsequent
        // invocation - i.e. the fresh retry we're testing for - succeeds immediately.
        TaskCompletionSource<Response<BlobContainerInfo>> initTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<BlobContainerClient> containerClientMock = CreateContainerClientMock();
        containerClientMock
            .SetupSequence(c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(initTcs.Task)
            .ReturnsAsync((Response<BlobContainerInfo>)null!);

        BlobPayloadStore store = new(new LargePayloadStorageOptions(), containerClientMock.Object);

        using CancellationTokenSource cts1 = new();
        using CancellationTokenSource cts2 = new();
        Task<string> upload1 = store.UploadAsync("payload", cts1.Token);
        Task<string> upload2 = store.UploadAsync("payload", cts2.Token);

        // Act: every caller waiting on the shared initialization abandons it (cancels its own
        // token) before that shared initialization itself later fails. No caller remains to
        // observe the failure directly, so only the initialization task's own completion path
        // can self-heal the cache.
        cts1.Cancel();
        cts2.Cancel();
        Func<Task> awaitUpload1 = () => upload1;
        Func<Task> awaitUpload2 = () => upload2;
        await awaitUpload1.Should().ThrowAsync<OperationCanceledException>();
        await awaitUpload2.Should().ThrowAsync<OperationCanceledException>();

        initTcs.SetException(new RequestFailedException(503, "Service unavailable"));

        // Give the shared initialization task's own continuation a chance to run and self-heal
        // the cache, even though no caller is left waiting on it.
        await Task.Delay(100);

        // Assert: a brand-new upload gets a fresh initialization attempt instead of reusing the
        // now-stale failed one.
        string token = await store.UploadAsync("payload", CancellationToken.None);
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
        await Task.Delay(20);

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

    /// <summary>
    /// Proves the fault-observation pattern relied on by the netstandard2.0-only cancellation
    /// branch of <c>BlobPayloadStore.WaitForInitializationAsync</c>: when a caller abandons the
    /// shared initialization task because its own cancellation token fires first, it attaches a
    /// fire-and-forget <see cref="TaskContinuationOptions.OnlyOnFaulted"/> continuation that
    /// touches <see cref="Task.Exception"/> so the shared task's eventual fault is always
    /// "observed" - even if every caller abandons it this way - and never surfaces via
    /// <see cref="TaskScheduler.UnobservedTaskException"/> when the task is later finalized.
    /// This test cannot exercise the netstandard2.0-only source directly (this test project
    /// targets a single runnable framework, not netstandard2.0), so it instead verifies the
    /// underlying pattern in isolation.
    /// </summary>
    [Fact]
    public async Task FaultObservingContinuation_PreventsUnobservedTaskException()
    {
        // Arrange
        TaskCompletionSource<bool> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool unobserved = false;
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            unobserved = true;
            e.SetObserved();
        }

        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            // Act: apply the same "abandon but still observe on fault" pattern used by
            // WaitForInitializationAsync's netstandard2.0 cancellation branch, then let the
            // shared task fault after it has already been abandoned.
            Task sharedTask = tcs.Task;
            _ = sharedTask.ContinueWith(
                static t => _ = t.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            tcs.SetException(new InvalidOperationException("boom"));

            // Let the fault-observing continuation run.
            await Task.Delay(50);

            // Drop the last strong reference and force finalization, which is when the runtime
            // reports any still-unobserved task exceptions.
            sharedTask = null!;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }

        // Assert: the fault was observed, so it was never reported as unobserved.
        unobserved.Should().BeFalse();
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
