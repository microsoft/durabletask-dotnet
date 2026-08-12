// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Entities;
using Microsoft.DurableTask.Entities.Tests;
using Xunit;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests.AutoPurge;

public class BlobPurgeJobTests
{
    readonly BlobPurgeJob job = new(new TestLogger<BlobPurgeJob>());

    [Fact]
    public async Task Create_WhenStopped_ActivatesJobAndStoresBatchSize()
    {
        // Arrange
        TestEntityOperation operation = new(
            nameof(BlobPurgeJob.Create),
            new TestEntityState(null),
            250);

        // Act
        await this.job.RunAsync(operation);

        // Assert
        BlobPurgeJobState state = Assert.IsType<BlobPurgeJobState>(
            operation.State.GetState(typeof(BlobPurgeJobState)));
        state.Status.Should().Be(BlobPurgeJobStatus.Active);
        state.PurgeBatchSize.Should().Be(250);
        state.CreatedAt.Should().NotBeNull();
        state.LastModifiedAt.Should().NotBeNull();

        // Starting the job means signalling Run. Asserted here so that the Times.Never assertion in the
        // already-active test below is meaningful rather than passing because the mock records nothing.
        Mock.Get(operation.Context).Verify(
            c => c.SignalEntity(
                It.IsAny<EntityInstanceId>(),
                nameof(BlobPurgeJob.Run),
                It.IsAny<object?>(),
                It.IsAny<SignalEntityOptions?>()),
            Times.Once);
    }

    [Fact]
    public async Task Create_WhenAlreadyActive_UpdatesBatchSizeWithoutRestarting()
    {
        // Arrange - the job is already running and the configured batch size has changed. Create is the only
        // path by which a new batch size can reach an active job, so it must be taken even though the job is
        // not restarted.
        BlobPurgeJobState existing = new()
        {
            Status = BlobPurgeJobStatus.Active,
            PurgeBatchSize = 100,
        };
        TestEntityOperation operation = new(
            nameof(BlobPurgeJob.Create),
            new TestEntityState(existing),
            999);

        // Act
        await this.job.RunAsync(operation);

        // Assert
        BlobPurgeJobState state = Assert.IsType<BlobPurgeJobState>(
            operation.State.GetState(typeof(BlobPurgeJobState)));
        state.Status.Should().Be(BlobPurgeJobStatus.Active);
        state.PurgeBatchSize.Should().Be(999);

        // Run is not re-signalled: the orchestrator is already up, and scheduling a second one over a live one
        // would terminate and replace it mid-work.
        Mock.Get(operation.Context).Verify(
            c => c.SignalEntity(
                It.IsAny<EntityInstanceId>(),
                It.IsAny<string>(),
                It.IsAny<object?>(),
                It.IsAny<SignalEntityOptions?>()),
            Times.Never);
    }

    [Fact]
    public async Task Stop_WhenActive_MovesToPendingAndKeepsHistory()
    {
        // Arrange - a running job. Stopping must not discard the configuration or the progress counters: they
        // are wanted if the job is started again, and CreatedAt is what distinguishes a stopped job from one
        // that was never started.
        DateTimeOffset createdAt = DateTimeOffset.UtcNow.AddDays(-3);
        BlobPurgeJobState existing = new()
        {
            Status = BlobPurgeJobStatus.Active,
            CreatedAt = createdAt,
            PurgedCount = 17,
            PurgeBatchSize = 250,
        };
        TestEntityOperation operation = new(
            nameof(BlobPurgeJob.Stop),
            new TestEntityState(existing),
            null);

        // Act
        await this.job.RunAsync(operation);

        // Assert
        BlobPurgeJobState state = Assert.IsType<BlobPurgeJobState>(
            operation.State.GetState(typeof(BlobPurgeJobState)));
        state.Status.Should().Be(BlobPurgeJobStatus.Pending);
        state.CreatedAt.Should().Be(createdAt);
        state.PurgedCount.Should().Be(17);
        state.PurgeBatchSize.Should().Be(250);
        state.LastModifiedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Stop_WhenNotActive_LeavesStateUntouched()
    {
        // Arrange - a job that is already stopped. Every host with auto-purge disabled signals Stop on each
        // start, so the repeat is the common case; rewriting LastModifiedAt each time would destroy its only
        // useful meaning, which is when the job actually stopped.
        DateTimeOffset stoppedAt = DateTimeOffset.UtcNow.AddHours(-6);
        BlobPurgeJobState existing = new()
        {
            Status = BlobPurgeJobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
            LastModifiedAt = stoppedAt,
            PurgeBatchSize = 250,
        };
        TestEntityOperation operation = new(
            nameof(BlobPurgeJob.Stop),
            new TestEntityState(existing),
            null);

        // Act
        await this.job.RunAsync(operation);

        // Assert
        BlobPurgeJobState state = Assert.IsType<BlobPurgeJobState>(
            operation.State.GetState(typeof(BlobPurgeJobState)));
        state.Status.Should().Be(BlobPurgeJobStatus.Pending);
        state.LastModifiedAt.Should().Be(stoppedAt);
        state.PurgeBatchSize.Should().Be(250);
    }

    [Fact]
    public async Task Create_StoresBatchSizeVerbatim_WithoutCoercion()
    {
        // Arrange - the batch size is validated once at specification (LargePayloadStorageOptions), so the
        // entity trusts its input and performs no coercion of its own. A zero here is stored as-is, proving
        // the previous non-positive-to-default fallback was removed.
        TestEntityOperation operation = new(
            nameof(BlobPurgeJob.Create),
            new TestEntityState(null),
            0);

        // Act
        await this.job.RunAsync(operation);

        // Assert
        BlobPurgeJobState state = Assert.IsType<BlobPurgeJobState>(
            operation.State.GetState(typeof(BlobPurgeJobState)));
        state.PurgeBatchSize.Should().Be(0);
    }

    [Fact]
    public async Task Get_ReturnsCurrentState()
    {
        // Arrange
        BlobPurgeJobState existing = new()
        {
            Status = BlobPurgeJobStatus.Active,
            PurgeBatchSize = 42,
        };
        TestEntityOperation operation = new(
            nameof(BlobPurgeJob.Get),
            new TestEntityState(existing),
            null);

        // Act
        object? result = await this.job.RunAsync(operation);

        // Assert
        BlobPurgeJobState state = Assert.IsType<BlobPurgeJobState>(result);
        state.Status.Should().Be(BlobPurgeJobStatus.Active);
        state.PurgeBatchSize.Should().Be(42);
    }
}
