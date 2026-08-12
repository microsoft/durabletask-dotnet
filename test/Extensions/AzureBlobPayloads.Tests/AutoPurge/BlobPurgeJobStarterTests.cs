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
    public async Task StartAsync_WhenAutoPurgeDisabled_SignalsJobToStop()
    {
        // Arrange - auto-purge is off. Returning silently was the whole disable bug: the job is a perpetual
        // orchestrator owned by the task hub, so one created while the flag was on keeps deleting blobs no
        // matter how many hosts start with it off. The disable path has to reach the backend.
        BlobPayloadStore store = new(new LargePayloadStorageOptions("UseDevelopmentStorage=true"));

        // Act
        (EntityInstanceId Id, string Operation)? signal = await SignalFromDisabledStarterAsync(store);

        // Assert
        signal.Should().NotBeNull("the disable path must tell the job to stop");
        signal!.Value.Id.Should().Be(new EntityInstanceId(nameof(BlobPurgeJob), BlobPurgeConstants.JobId));
        signal.Value.Operation.Should().Be(nameof(BlobPurgeJob.Stop));
    }

    [Fact]
    public async Task StartAsync_WhenAutoPurgeDisabledAndStoreCannotDelete_StillSignalsJobToStop()
    {
        // Arrange - the store-capability gate deliberately does not apply to the disable path. Stopping a job
        // requires no ability to delete anything, and a user who has switched to a store that cannot delete is
        // precisely the user whose still-running job must be stopped.

        // Act
        (EntityInstanceId Id, string Operation)? signal =
            await SignalFromDisabledStarterAsync(new NonDeletingPayloadStore());

        // Assert
        signal.Should().NotBeNull("a store that cannot delete must not block stopping the job");
        signal!.Value.Operation.Should().Be(nameof(BlobPurgeJob.Stop));
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
    /// Runs a starter with auto-purge disabled and returns the entity signal it emitted, or null if none was
    /// emitted within the timeout. The signal runs on a background task, so it is awaited rather than assumed.
    /// </summary>
    static async Task<(EntityInstanceId Id, string Operation)?> SignalFromDisabledStarterAsync(PayloadStore store)
    {
        Mock<DurableEntityClient> entities = new("test");
        TaskCompletionSource<(EntityInstanceId Id, string Operation)> signalled = new();
        entities
            .Setup(e => e.SignalEntityAsync(
                It.IsAny<EntityInstanceId>(),
                It.IsAny<string>(),
                It.IsAny<object?>(),
                It.IsAny<SignalEntityOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<EntityInstanceId, string, object?, SignalEntityOptions?, CancellationToken>(
                (id, operation, _, _, _) => signalled.TrySetResult((id, operation)))
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

        await starter.StartAsync(CancellationToken.None);
        Task completed = await Task.WhenAny(signalled.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        await starter.StopAsync(CancellationToken.None);

        return completed == signalled.Task ? await signalled.Task : null;
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
