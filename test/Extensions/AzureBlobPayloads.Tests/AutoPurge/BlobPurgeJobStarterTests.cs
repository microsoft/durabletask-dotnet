// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests.AutoPurge;

public class BlobPurgeJobStarterTests
{
    [Fact]
    public async Task StartAsync_WhenStoreCannotDelete_DoesNotStartJobOrTouchClient()
    {
        // Arrange - the registered store is not the blob store, so its DeleteAsync is unsupported. Every payload
        // would fail, so the starter must refuse to run rather than spin against the backend. AutoPurge is on so
        // the run gets past the opt-in gate and reaches the store-capability gate under test.
        Mock<IDurableTaskClientProvider> provider = new();
        BlobPurgeJobStarter starter = new(
            provider.Object,
            new NonDeletingPayloadStore(),
            OptionsFor(new LargePayloadStorageOptions { AutoPurge = true }),
            "test",
            new TestLogger<BlobPurgeJobStarter>());

        // Act
        await starter.StartAsync(CancellationToken.None);

        // Assert - the store gate short-circuits before the client is resolved, so the provider is never asked
        // for a client.
        provider.Verify(p => p.GetClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_WhenStoreIsBlobStore_DoesNotShortCircuit()
    {
        // Arrange - the real blob store with auto-purge enabled. UseDevelopmentStorage=true is a valid connection
        // string that constructs the store offline (no network I/O), so resolving it at host start is safe.
        BlobPayloadStore store = new(new LargePayloadStorageOptions("UseDevelopmentStorage=true"));
        Mock<IDurableTaskClientProvider> provider = new();
        provider.Setup(p => p.GetClient(It.IsAny<string>())).Returns(new Mock<DurableTaskClient>("test").Object);
        TestLogger<BlobPurgeJobStarter> logger = new();
        BlobPurgeJobStarter starter = new(
            provider.Object,
            store,
            OptionsFor(new LargePayloadStorageOptions("UseDevelopmentStorage=true") { AutoPurge = true }),
            "test",
            logger);

        // Act
        await starter.StartAsync(CancellationToken.None);

        // Assert - neither gate fired: no store-cannot-delete log, and the starter proceeded to resolve the
        // client and start its background ensure path (which runs on a background task, so timing is not
        // asserted).
        logger.Logs.Should().NotContain(entry => entry.Message.Contains("is not an Azure Blob payload store"));
        provider.Verify(p => p.GetClient(It.IsAny<string>()), Times.Once);

        // Cleanup - cancel the background ensure task so it does not outlive the test.
        await starter.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_WhenAutoPurgeDisabledAndJobIsActive_SignalsJobToStop()
    {
        // Arrange - auto-purge is off while a job is running. Returning silently was the whole disable bug: the
        // job is a perpetual orchestrator owned by the task hub, so one created while the flag was on keeps
        // deleting blobs no matter how many hosts start with it off. The disable path has to reach the backend.
        BlobPayloadStore store = new(new LargePayloadStorageOptions("UseDevelopmentStorage=true"));

        // Act
        (( EntityInstanceId Id, string Operation)? Signal, int Reads) run =
            await RunDisabledStarterAsync(store, () => MetadataFor(BlobPurgeJobStatus.Active));

        // Assert
        run.Signal.Should().NotBeNull("the disable path must tell a running job to stop");
        run.Signal!.Value.Id.Should().Be(new EntityInstanceId(nameof(BlobPurgeJob), BlobPurgeConstants.JobId));
        run.Signal.Value.Operation.Should().Be(nameof(BlobPurgeJob.Stop));
    }

    [Fact]
    public async Task StartAsync_WhenAutoPurgeDisabledAndNoJobExists_DoesNotSignal()
    {
        // Arrange - the common case by far: an app that externalizes payloads and never enabled auto-purge.
        // Signalling regardless would create the job entity, because the framework persists entity state after
        // every operation, so an app that never used the feature would still carry one entity per task hub.
        BlobPayloadStore store = new(new LargePayloadStorageOptions("UseDevelopmentStorage=true"));

        // Act
        (( EntityInstanceId Id, string Operation)? Signal, int Reads) run =
            await RunDisabledStarterAsync(store, () => null);

        // Assert - Reads is the positive control. A null signal is also what a mock that was never reached
        // produces, so the assertion that nothing was signalled is only meaningful alongside proof that the
        // code did run and did ask. The same wiring signals when the job is active, which the test above pins.
        run.Reads.Should().Be(1, "the starter must consult the job state rather than skipping the path entirely");
        run.Signal.Should().BeNull("there is no job to stop, so nothing may be created");
    }

    [Fact]
    public async Task StartAsync_WhenAutoPurgeDisabledAndJobIsAlreadyStopped_DoesNotSignal()
    {
        // Arrange - a job that has already been stopped needs nothing. The entity would ignore the signal, but
        // the framework still persists state on every operation, so signalling would write on every host start.
        BlobPayloadStore store = new(new LargePayloadStorageOptions("UseDevelopmentStorage=true"));

        // Act
        (( EntityInstanceId Id, string Operation)? Signal, int Reads) run =
            await RunDisabledStarterAsync(store, () => MetadataFor(BlobPurgeJobStatus.Pending));

        // Assert
        run.Reads.Should().Be(1);
        run.Signal.Should().BeNull("a job that is not active must not be signalled on every restart");
    }

    [Fact]
    public async Task StartAsync_WhenAutoPurgeDisabledAndEntityHasNoState_DoesNotSignal()
    {
        // Arrange - an entity whose state has been cleared still reports as existing until entity storage is
        // cleaned, and its metadata carries no state at all. Reading EntityMetadata.State in that condition
        // throws, so the status has to be reached through IncludesState. Such an entity has no running job.
        BlobPayloadStore store = new(new LargePayloadStorageOptions("UseDevelopmentStorage=true"));

        // Act
        (( EntityInstanceId Id, string Operation)? Signal, int Reads) run = await RunDisabledStarterAsync(
            store,
            () => new EntityMetadata<BlobPurgeJobState>(
                new EntityInstanceId(nameof(BlobPurgeJob), BlobPurgeConstants.JobId)));

        // Assert
        run.Reads.Should().Be(1);
        run.Signal.Should().BeNull("an entity with no state has no running job");
    }

    [Fact]
    public async Task StartAsync_WhenAutoPurgeDisabledAndStateCannotBeRead_SignalsJobToStopAnyway()
    {
        // Arrange - the pre-check is an optimization; stopping the job is the correctness-critical outcome. A
        // client that cannot answer the query must not be able to prevent the stop, or the optimization's own
        // failure mode would defeat the fix it is attached to. Retrying instead of falling through would be
        // just as bad for a permanent failure: the loop would spin and the running job would never be told.
        BlobPayloadStore store = new(new LargePayloadStorageOptions("UseDevelopmentStorage=true"));

        // Act
        (( EntityInstanceId Id, string Operation)? Signal, int Reads) run = await RunDisabledStarterAsync(
            store,
            () => throw new NotSupportedException("entity queries are not supported"));

        // Assert
        run.Signal.Should().NotBeNull("an unreadable state must not suppress the stop");
        run.Signal!.Value.Operation.Should().Be(nameof(BlobPurgeJob.Stop));
    }

    [Fact]
    public async Task StartAsync_WhenAutoPurgeDisabledAndStoreCannotDelete_StillSignalsJobToStop()
    {
        // Arrange - the store-capability gate deliberately does not apply to the disable path. Stopping a job
        // requires no ability to delete anything, and a user who has switched to a store that cannot delete is
        // precisely the user whose still-running job must be stopped.

        // Act
        (( EntityInstanceId Id, string Operation)? Signal, int Reads) run = await RunDisabledStarterAsync(
            new NonDeletingPayloadStore(), () => MetadataFor(BlobPurgeJobStatus.Active));

        // Assert
        run.Signal.Should().NotBeNull("a store that cannot delete must not block stopping the job");
        run.Signal!.Value.Operation.Should().Be(nameof(BlobPurgeJob.Stop));
    }

    [Fact]
    public async Task EnsureJob_SchedulesBridge_DedupingOnlyPendingAndRunning()
    {
        // Arrange - the dedupe list is an inverted whitelist: the wire policy is (all statuses - dedupe), so a
        // status omitted from the call silently becomes replaceable. Nothing in the compiler or the type system
        // catches that, so the exact set is pinned here.
        BlobPayloadStore store = new(new LargePayloadStorageOptions("UseDevelopmentStorage=true"));
        Mock<DurableTaskClient> client = new("test");
        TaskCompletionSource<StartOrchestrationOptions?> scheduled = new();
        client
            .Setup(c => c.ScheduleNewOrchestrationInstanceAsync(
                It.IsAny<TaskName>(),
                It.IsAny<object?>(),
                It.IsAny<StartOrchestrationOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<TaskName, object?, StartOrchestrationOptions?, CancellationToken>(
                (_, _, options, _) => scheduled.TrySetResult(options))
            .ReturnsAsync(BlobPurgeConstants.StarterInstanceId);

        Mock<IDurableTaskClientProvider> provider = new();
        provider.Setup(p => p.GetClient(It.IsAny<string>())).Returns(client.Object);
        BlobPurgeJobStarter starter = new(
            provider.Object,
            store,
            OptionsFor(new LargePayloadStorageOptions("UseDevelopmentStorage=true") { AutoPurge = true }),
            "test",
            new TestLogger<BlobPurgeJobStarter>());

        // Act - the ensure work runs on a background task, so wait for the scheduling call rather than assuming
        // it already happened.
        await starter.StartAsync(CancellationToken.None);
        Task completed = await Task.WhenAny(scheduled.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        await starter.StopAsync(CancellationToken.None);

        // Assert
        completed.Should().BeSameAs(scheduled.Task, "the starter must schedule the bridge orchestration");
        StartOrchestrationOptions? options = await scheduled.Task;
        options.Should().NotBeNull();
        options!.InstanceId.Should().Be(BlobPurgeConstants.StarterInstanceId);

        // Every status other than Pending and Running is replaceable, so a finished bridge is re-run on the
        // next host start. That is what lets the job rebuild itself after the entity is removed. The set is
        // asserted exactly, never as a superset, because the hazard is a silent omission.
        options.DedupeStatuses.Should().BeEquivalentTo(["Pending", "Running"]);
    }

    static IOptionsMonitor<LargePayloadStorageOptions> OptionsFor(LargePayloadStorageOptions options)
    {
        Mock<IOptionsMonitor<LargePayloadStorageOptions>> monitor = new();
        monitor.Setup(m => m.Get(It.IsAny<string>())).Returns(options);
        return monitor.Object;
    }

    /// <summary>
    /// Builds entity metadata carrying a job in the given status.
    /// </summary>
    static EntityMetadata<BlobPurgeJobState> MetadataFor(BlobPurgeJobStatus status) =>
        new(
            new EntityInstanceId(nameof(BlobPurgeJob), BlobPurgeConstants.JobId),
            new BlobPurgeJobState { Status = status });

    /// <summary>
    /// Runs a starter with auto-purge disabled, using <paramref name="read"/> as the job state the client
    /// reports (it may return null for an absent entity, or throw to simulate a client that cannot answer).
    /// Returns the entity signal the starter emitted, if any, and how many times the state was read.
    /// </summary>
    static async Task<(( EntityInstanceId Id, string Operation)? Signal, int Reads)> RunDisabledStarterAsync(
        PayloadStore store, Func<EntityMetadata<BlobPurgeJobState>?> read)
    {
        Mock<DurableEntityClient> entities = new("test");
        TaskCompletionSource<bool> readCalled = new();
        (EntityInstanceId Id, string Operation)? signal = null;
        int reads = 0;

        // The three-argument overload is set up because that is the one the starter calls. The shorter
        // GetEntityAsync(id, cancellation) is virtual, so a mock overrides it instead of forwarding, and a
        // setup placed on the abstract method alone would silently never match.
        entities
            .Setup(e => e.GetEntityAsync<BlobPurgeJobState>(
                It.IsAny<EntityInstanceId>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                reads++;
                readCalled.TrySetResult(true);
                return Task.FromResult(read());
            });

        entities
            .Setup(e => e.SignalEntityAsync(
                It.IsAny<EntityInstanceId>(),
                It.IsAny<string>(),
                It.IsAny<object?>(),
                It.IsAny<SignalEntityOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<EntityInstanceId, string, object?, SignalEntityOptions?, CancellationToken>(
                (id, operation, _, _, _) => signal = (id, operation))
            .Returns(Task.CompletedTask);

        Mock<DurableTaskClient> client = new("test");
        client.Setup(c => c.Entities).Returns(entities.Object);
        Mock<IDurableTaskClientProvider> provider = new();
        provider.Setup(p => p.GetClient(It.IsAny<string>())).Returns(client.Object);

        BlobPurgeJobStarter starter = new(
            provider.Object,
            store,
            OptionsFor(new LargePayloadStorageOptions("UseDevelopmentStorage=true") { AutoPurge = false }),
            "test",
            new TestLogger<BlobPurgeJobStarter>());

        // The work runs on a background task, so wait for the read before shutting down. StopAsync then waits
        // for that task to finish, which makes the observation deterministic: by the time it returns the
        // starter has either signalled or decided not to, rather than being timed out mid-decision.
        await starter.StartAsync(CancellationToken.None);
        await Task.WhenAny(readCalled.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        await starter.StopAsync(CancellationToken.None);

        return (signal, reads);
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
