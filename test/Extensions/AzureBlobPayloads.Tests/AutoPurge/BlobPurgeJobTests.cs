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

        // Starting the job means signalling Run, and this path has always done so. Asserted explicitly because
        // the already-active path now signals Run too, which makes this the case that would silently stop being
        // covered if the two branches were ever collapsed.
        Mock.Get(operation.Context).Verify(
            c => c.SignalEntity(
                It.IsAny<EntityInstanceId>(),
                nameof(BlobPurgeJob.Run),
                It.IsAny<object?>(),
                It.IsAny<SignalEntityOptions?>()),
            Times.Once);
    }

    [Fact]
    public async Task Create_WhenAlreadyActive_UpdatesBatchSizeAndReSignalsRun()
    {
        // Arrange - the job is already running and the configured batch size has changed. Create is the only
        // path by which a new batch size can reach an active job, so it must be taken.
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

        // Run is re-signalled even though the job is already active. That is what lets a job whose orchestrator
        // has died be rebuilt at the next reconciliation pass, and it is safe because the backend discards the
        // resulting start while the orchestrator is alive rather than replacing it.
        Mock.Get(operation.Context).Verify(
            c => c.SignalEntity(
                It.IsAny<EntityInstanceId>(),
                nameof(BlobPurgeJob.Run),
                It.IsAny<object?>(),
                It.IsAny<SignalEntityOptions?>()),
            Times.Once);
    }

    [Fact]
    public async Task Create_WhenAlreadyActive_SignalsNothingOtherThanRun()
    {
        // Arrange - pins that re-signalling Run is the only signal the already-active path emits. Verifying the
        // Run signal alone would still pass if a second, different signal were added beside it.
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

        // Assert - the Times.Once above is the positive control for this Times.Never: both target the same
        // four-argument overload on the same mock, so this cannot be passing because nothing was recorded.
        Mock.Get(operation.Context).Verify(
            c => c.SignalEntity(
                It.IsAny<EntityInstanceId>(),
                It.Is<string>(name => name != nameof(BlobPurgeJob.Run)),
                It.IsAny<object?>(),
                It.IsAny<SignalEntityOptions?>()),
            Times.Never);
    }

    [Fact]
    public async Task Create_WhenAlreadyActive_AndBatchSizeUnchanged_DoesNotMoveLastModifiedAt()
    {
        // Arrange - the steady state. Create runs on every reconciliation pass, and almost every one of those
        // carries the same configured batch size the job already has. If that rewrote LastModifiedAt, the field
        // would degrade to "time of the last pass" and say nothing about the job.
        DateTimeOffset configuredAt = DateTimeOffset.UtcNow.AddDays(-2);
        BlobPurgeJobState existing = new()
        {
            Status = BlobPurgeJobStatus.Active,
            LastModifiedAt = configuredAt,
            PurgeBatchSize = 250,
        };
        TestEntityOperation operation = new(
            nameof(BlobPurgeJob.Create),
            new TestEntityState(existing),
            250);

        // Act
        await this.job.RunAsync(operation);

        // Assert - the sibling test below is the positive control: identical wiring, differing only in the
        // batch size passed in, and it proves this same path does move the field when something changes.
        BlobPurgeJobState state = Assert.IsType<BlobPurgeJobState>(
            operation.State.GetState(typeof(BlobPurgeJobState)));
        state.LastModifiedAt.Should().Be(configuredAt);
        state.PurgeBatchSize.Should().Be(250);
    }

    [Fact]
    public async Task Create_WhenAlreadyActive_AndBatchSizeChanged_MovesLastModifiedAt()
    {
        // Arrange - a real configuration change reaching an active job, which is the one thing this path
        // exists to deliver and the one case that must be recorded.
        DateTimeOffset configuredAt = DateTimeOffset.UtcNow.AddDays(-2);
        BlobPurgeJobState existing = new()
        {
            Status = BlobPurgeJobStatus.Active,
            LastModifiedAt = configuredAt,
            PurgeBatchSize = 250,
        };
        TestEntityOperation operation = new(
            nameof(BlobPurgeJob.Create),
            new TestEntityState(existing),
            500);

        // Act
        await this.job.RunAsync(operation);

        // Assert
        BlobPurgeJobState state = Assert.IsType<BlobPurgeJobState>(
            operation.State.GetState(typeof(BlobPurgeJobState)));
        state.PurgeBatchSize.Should().Be(500);
        state.LastModifiedAt.Should().BeAfter(configuredAt);
    }

    [Fact]
    public async Task Run_DoesNotMoveLastModifiedAt()
    {
        // Arrange - Run schedules an orchestrator and changes nothing about the job. It is signalled by every
        // Create, so writing here would move the field on every reconciliation pass and undo the conditional
        // write above.
        DateTimeOffset configuredAt = DateTimeOffset.UtcNow.AddDays(-2);
        BlobPurgeJobState existing = new()
        {
            Status = BlobPurgeJobStatus.Active,
            LastModifiedAt = configuredAt,
            PurgeBatchSize = 250,
        };
        TestEntityOperation operation = new(
            nameof(BlobPurgeJob.Run),
            new TestEntityState(existing),
            null);

        // Act
        await this.job.RunAsync(operation);

        // Assert - scheduling is asserted first as the positive control. Without it a misdispatched operation
        // would write nothing and pass this test for entirely the wrong reason.
        Mock.Get(operation.Context).Verify(
            c => c.ScheduleNewOrchestration(
                It.IsAny<TaskName>(),
                It.IsAny<object?>(),
                It.IsAny<StartOrchestrationOptions?>()),
            Times.Once);

        BlobPurgeJobState state = Assert.IsType<BlobPurgeJobState>(
            operation.State.GetState(typeof(BlobPurgeJobState)));
        state.LastModifiedAt.Should().Be(configuredAt);
    }

    [Fact]
    public async Task Run_WhenActive_SchedulesOrchestratorAtTheFixedInstanceId()
    {
        // Arrange - the fixed instance ID is the mechanism the whole restart story rests on. It is what lets the
        // backend recognize a start as targeting the existing orchestrator, and therefore discard it while that
        // orchestrator is alive instead of running a second one alongside it.
        BlobPurgeJobState existing = new()
        {
            Status = BlobPurgeJobStatus.Active,
            PurgeBatchSize = 250,
        };
        TestEntityOperation operation = new(
            nameof(BlobPurgeJob.Run),
            new TestEntityState(existing),
            null);
        Mock.Get(operation.Context)
            .Setup(c => c.Id)
            .Returns(new EntityInstanceId(nameof(BlobPurgeJob), BlobPurgeConstants.JobId));

        // Act
        await this.job.RunAsync(operation);

        // Assert
        Mock.Get(operation.Context).Verify(
            c => c.ScheduleNewOrchestration(
                It.IsAny<TaskName>(),
                It.IsAny<object?>(),
                It.Is<StartOrchestrationOptions>(o =>
                    o.InstanceId == BlobPurgeConstants.GetOrchestratorInstanceId(BlobPurgeConstants.JobId))),
            Times.Once);
    }

    [Fact]
    public async Task Run_WhenNotActive_SchedulesNothing()
    {
        // Arrange - a Run signal arriving after the job was stopped. Create signals Run rather than starting the
        // orchestrator itself, so a Stop landing between the two leaves this signal in flight against a job that
        // must no longer purge. This guard is what makes the stop win instead of the stale signal restarting it.
        BlobPurgeJobState existing = new()
        {
            Status = BlobPurgeJobStatus.Pending,
            PurgeBatchSize = 250,
        };
        TestEntityOperation operation = new(
            nameof(BlobPurgeJob.Run),
            new TestEntityState(existing),
            null);

        // Act
        await this.job.RunAsync(operation);

        // Assert - the Times.Once above is the positive control: same mocked type, same three-argument overload,
        // so this cannot be passing merely because the mock records nothing.
        Mock.Get(operation.Context).Verify(
            c => c.ScheduleNewOrchestration(
                It.IsAny<TaskName>(),
                It.IsAny<object?>(),
                It.IsAny<StartOrchestrationOptions?>()),
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
        // Arrange - a job that is already stopped. The starter's client-side pre-check normally suppresses a
        // redundant stop, so this is the signal that races past it: the job stopped between that read and this
        // signal landing. Rewriting LastModifiedAt here would report the losing side of that race as if it
        // were the moment the job stopped.
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

    [Fact]
    public async Task Create_WhenUnsupported_RevivesJob()
    {
        // Arrange - the backend was found not to implement the purge RPCs, so the job was disabled. Create is
        // now reached only from an explicit client call, so reviving is exactly what the caller asked for, and
        // it is the documented recovery once the backend has been upgraded. Refusing here would strand the job
        // permanently, because nothing else clears the status.
        BlobPurgeJobState existing = new()
        {
            Status = BlobPurgeJobStatus.Unsupported,
            LastError = "backend does not implement GetLargePayloadTombstones",
            PurgeBatchSize = 250,
        };
        TestEntityOperation operation = new(
            nameof(BlobPurgeJob.Create),
            new TestEntityState(existing),
            250);

        // Act
        await this.job.RunAsync(operation);

        // Assert - reviving means both halves: the status goes Active and the orchestrator is scheduled. The
        // stale error is dropped so a later failure is not read as this one.
        BlobPurgeJobState state = Assert.IsType<BlobPurgeJobState>(
            operation.State.GetState(typeof(BlobPurgeJobState)));
        state.Status.Should().Be(BlobPurgeJobStatus.Active);
        state.LastError.Should().BeNull();

        Mock.Get(operation.Context).Verify(
            c => c.SignalEntity(
                It.IsAny<EntityInstanceId>(),
                nameof(BlobPurgeJob.Run),
                It.IsAny<object?>(),
                It.IsAny<SignalEntityOptions?>()),
            Times.Once);
    }

    [Fact]
    public async Task MarkUnsupported_WhenActive_DisablesJobAndRecordsDetail()
    {
        // Arrange - a running job whose fetch/report activity just surfaced a gRPC Unimplemented. The job must
        // move to a status Create refuses to revive, and the detail is retained so an operator can see why.
        BlobPurgeJobState existing = new()
        {
            Status = BlobPurgeJobStatus.Active,
            PurgeBatchSize = 250,
        };
        TestEntityOperation operation = new(
            nameof(BlobPurgeJob.MarkUnsupported),
            new TestEntityState(existing),
            "backend does not implement GetLargePayloadTombstones");

        // Act
        await this.job.RunAsync(operation);

        // Assert
        BlobPurgeJobState state = Assert.IsType<BlobPurgeJobState>(
            operation.State.GetState(typeof(BlobPurgeJobState)));
        state.Status.Should().Be(BlobPurgeJobStatus.Unsupported);
        state.LastError.Should().Be("backend does not implement GetLargePayloadTombstones");
        state.LastModifiedAt.Should().NotBeNull();

        // Unlike Create, this must not (re)start the orchestrator - the job is being disabled, not run.
        Mock.Get(operation.Context).Verify(
            c => c.SignalEntity(
                It.IsAny<EntityInstanceId>(),
                nameof(BlobPurgeJob.Run),
                It.IsAny<object?>(),
                It.IsAny<SignalEntityOptions?>()),
            Times.Never);
    }

    [Fact]
    public async Task MarkUnsupported_WhenAlreadyUnsupported_LeavesStateUntouched()
    {
        // Arrange - a second replica reporting the same unsupported backend before the first orchestrator has
        // exited. The repeat must be a no-op so LastModifiedAt keeps meaning "when the job was disabled" and the
        // original detail is not overwritten by a later, possibly less specific, one.
        DateTimeOffset disabledAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        BlobPurgeJobState existing = new()
        {
            Status = BlobPurgeJobStatus.Unsupported,
            LastError = "original detail",
            LastModifiedAt = disabledAt,
            PurgeBatchSize = 250,
        };
        TestEntityOperation operation = new(
            nameof(BlobPurgeJob.MarkUnsupported),
            new TestEntityState(existing),
            "a different detail");

        // Act
        await this.job.RunAsync(operation);

        // Assert - the sibling test above is the positive control: identical wiring off an Active job does move
        // both fields, proving this no-op is the guard's doing and not a dead path.
        BlobPurgeJobState state = Assert.IsType<BlobPurgeJobState>(
            operation.State.GetState(typeof(BlobPurgeJobState)));
        state.Status.Should().Be(BlobPurgeJobStatus.Unsupported);
        state.LastError.Should().Be("original detail");
        state.LastModifiedAt.Should().Be(disabledAt);
    }
}
