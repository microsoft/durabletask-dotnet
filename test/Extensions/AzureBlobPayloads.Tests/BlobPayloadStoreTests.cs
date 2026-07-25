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

        // CreateContainerIfNotExistsAsync clears the cache (Interlocked.CompareExchange) in its
        // catch block strictly before re-throwing, and OnInitializationFaultObserved is invoked
        // from a continuation that only runs once that re-thrown exception has faulted the
        // shared task - so by the time this hook fires, self-healing has already happened. Using
        // it to synchronize here (rather than a fixed Task.Delay) is exactly as deterministic as
        // the timing it stands in for, with no arbitrary sleep to tune or risk racing under load.
        TaskCompletionSource<Exception> observedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        store.OnInitializationFaultObserved = ex => observedTcs.TrySetResult(ex);

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

        // Wait deterministically for the shared initialization task's own fault-observing
        // continuation to run (and, with it, self-heal the cache) instead of a fixed delay: a
        // generous timeout guards against the test hanging forever (instead of failing with a
        // clear message) if that continuation never runs at all - which would itself indicate a
        // regression in self-healing.
        Task completedTask = await Task.WhenAny(observedTcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        completedTask.Should().BeSameAs(observedTcs.Task, "the shared initializer's fault-observing continuation (and self-heal) should have run within the timeout");

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
    public void OnInitializationFaultObserved_DefaultsToNonNullNoOpAndRejectsNull()
    {
        // Arrange: construct a store exactly as production code does, without touching the hook.
        BlobPayloadStore store = new(new LargePayloadStorageOptions(), CreateContainerClientMock().Object);

        // Assert: the hook is never null - neither by default nor after explicitly assigning
        // null - and invoking the untouched default does nothing (it's a no-op), not throw.
        //
        // This is what actually proves the production-default path is safe, independent of any
        // test that overrides the hook. UploadAsync_AllWaitersCancelThenInitializerFaults_ExceptionIsObserved
        // (below) proves the fault-observing continuation runs and reads Task.Exception when a
        // custom hook is installed, but that alone can't distinguish "the continuation
        // unconditionally reads Task.Exception" from "it only does so because a hook happens to
        // be set" - which is exactly how the historical
        // `this.OnInitializationFaultObserved?.Invoke(t.Exception!)` regression passed every
        // existing test while silently never observing faults in production, where the hook was
        // null by default. This test closes that gap directly: because the hook can never be
        // null - by construction of this property, not by convention - the continuation's direct,
        // unconditional invocation of it (see ObserveFaultWithoutAwaiting) always evaluates
        // Task.Exception, whether or not any test has overridden the hook.
        store.OnInitializationFaultObserved.Should().NotBeNull();

        Action invokeDefault = () => store.OnInitializationFaultObserved(new InvalidOperationException("boom"));
        invokeDefault.Should().NotThrow();

        store.OnInitializationFaultObserved = null!;
        store.OnInitializationFaultObserved.Should().NotBeNull();
    }

    [Fact]
    public async Task UploadAsync_AllWaitersCancelThenInitializerFaults_ExceptionIsObserved()
    {
        // Arrange: control exactly when the shared initialization completes/fails, and use the
        // internal OnInitializationFaultObserved test hook to deterministically prove that the
        // fault-observing continuation attached in PublishNewInitializer actually ran and read
        // the shared task's Exception - rather than relying on TaskScheduler.UnobservedTaskException
        // plus a forced GC, which can't reliably distinguish "the fix ran" from "the CLR just
        // hasn't collected the task yet" (e.g. because the async state machine still roots it).
        // The continuation is attached once, up front, when the initializer is published, so this
        // applies uniformly regardless of which cancellation code path any given caller takes.
        //
        // Overriding the hook here with a custom delegate exercises the exact same statement -
        // "this.OnInitializationFaultObserved(t.Exception!)", no null-conditional - that runs
        // against the untouched, no-op default in production (see
        // OnInitializationFaultObserved_DefaultsToNonNullNoOpAndRejectsNull, which proves that
        // default can never be null): the only difference is which Action<Exception> instance
        // gets invoked, not whether it does.
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

        int observedCount = 0;
        TaskCompletionSource<Exception> observedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        store.OnInitializationFaultObserved = ex =>
        {
            Interlocked.Increment(ref observedCount);

            // TrySetResult (rather than blindly SetResult) tolerates the hook firing more than
            // once without throwing here; the exactly-once assertion below is what actually
            // proves it fired exactly once.
            observedTcs.TrySetResult(ex);
        };

        using CancellationTokenSource cts1 = new();
        using CancellationTokenSource cts2 = new();

        // Act: every caller of the still-pending shared initialization cancels its own token
        // before that shared initialization later faults, so no caller is left waiting on it
        // when the failure occurs.
        Task<string> upload1 = store.UploadAsync("payload", cts1.Token);
        Task<string> upload2 = store.UploadAsync("payload", cts2.Token);

        cts1.Cancel();
        cts2.Cancel();
        Func<Task> awaitUpload1 = () => upload1;
        Func<Task> awaitUpload2 = () => upload2;
        await awaitUpload1.Should().ThrowAsync<OperationCanceledException>();
        await awaitUpload2.Should().ThrowAsync<OperationCanceledException>();

        RequestFailedException expectedException = new(503, "Service unavailable");
        initTcs.SetException(expectedException);

        // Wait deterministically for the fault-observing continuation to run instead of polling:
        // it's attached with TaskContinuationOptions.ExecuteSynchronously, but SetException above
        // may itself run continuations asynchronously depending on scheduling, so await the TCS
        // it completes rather than assuming it already ran. A generous timeout guards against the
        // test hanging forever (instead of failing with a clear message) if the continuation never
        // runs at all - which would itself indicate a regression in the fix under test.
        Task completedTask = await Task.WhenAny(observedTcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        completedTask.Should().BeSameAs(observedTcs.Task, "the fault-observing continuation should have run within the timeout");
        Exception observedException = await observedTcs.Task;

        containerClientMock.Verify(
            c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert: the shared initializer's fault was observed exactly once, proving the
        // continuation ran and read Task.Exception - not merely that the runtime hasn't yet
        // reported it as unobserved. Task.Exception wraps the fault in an AggregateException,
        // so unwrap it to confirm it's the exact exception the initializer faulted with.
        observedCount.Should().Be(1);
        observedException.Should().BeOfType<AggregateException>();
        ((AggregateException)observedException).InnerException.Should().BeSameAs(expectedException);
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
