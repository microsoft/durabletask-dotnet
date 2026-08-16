// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Xunit;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests.AutoPurge;

public class BlobPurgeJobOrchestratorTests
{
    static readonly EntityInstanceId JobEntityId = new(nameof(BlobPurgeJob), BlobPurgeConstants.JobId);

    readonly List<int> requested = [];
    readonly TestLogger<BlobPurgeJobOrchestrator> logger = new();

    [Fact]
    public async Task RunAsync_UsesBatchSizeFromEntity_NotFromInput()
    {
        // Arrange - the orchestrator is perpetual, so its input is fixed at creation and carried verbatim
        // through every continue-as-new. Reading the batch size from the input would pin the value written by
        // the very first Create for the life of the job, which is what made a configuration change impossible
        // to apply. The entity is the authority.
        Mock<TaskOrchestrationContext> context = this.ContextFor(
            new BlobPurgeJobState { Status = BlobPurgeJobStatus.Active, PurgeBatchSize = 777 });

        // Act
        await new BlobPurgeJobOrchestrator().RunAsync(
            context.Object, new BlobPurgeJobRunRequest(JobEntityId, PurgeBatchSize: 100));

        // Assert
        this.AssertNoCycleFailed();
        this.requested.Should().Equal(777);
    }

    [Fact]
    public async Task RunAsync_WhenEntityBatchSizeIsUnset_FallsBackToInput()
    {
        // Arrange - an entity written by an older build carries no batch size at all. Passing that zero to the
        // fetch activity would ask the backend for nothing on every cycle: a silent, permanent stall.
        Mock<TaskOrchestrationContext> context = this.ContextFor(
            new BlobPurgeJobState { Status = BlobPurgeJobStatus.Active, PurgeBatchSize = 0 });

        // Act
        await new BlobPurgeJobOrchestrator().RunAsync(
            context.Object, new BlobPurgeJobRunRequest(JobEntityId, PurgeBatchSize: 100));

        // Assert
        this.AssertNoCycleFailed();
        this.requested.Should().Equal(100);
    }

    [Fact]
    public async Task RunAsync_WhenJobIsStopped_ExitsWithoutFetching()
    {
        // Arrange - Stop moves the entity off Active without touching this orchestrator, so the exit is the
        // orchestrator's own decision on its own schedule. Nothing must be fetched on a stopped job.
        Mock<TaskOrchestrationContext> context = this.ContextFor(
            new BlobPurgeJobState { Status = BlobPurgeJobStatus.Pending, PurgeBatchSize = 777 });

        // Act
        await new BlobPurgeJobOrchestrator().RunAsync(
            context.Object, new BlobPurgeJobRunRequest(JobEntityId, PurgeBatchSize: 100));

        // Assert - an empty fetch list is also what a broken mock produces, so the stopping log is asserted
        // too: it is the only evidence that the orchestrator read the state and chose to exit.
        this.AssertNoCycleFailed();
        this.requested.Should().BeEmpty();
        this.logger.Logs.Should().Contain(entry => entry.Message.Contains("stopping"));
    }

    [Fact]
    public async Task RunAsync_WhenBackendDoesNotImplementPurgeRpcs_DisablesJobAndExits()
    {
        // Arrange - the fetch activity surfaced a gRPC Unimplemented as NotImplementedException (mixed rollout /
        // stale emulator). The orchestrator must disable the job durably and exit its perpetual loop, rather
        // than logging a generic cycle failure and retrying on every backoff forever.
        Mock<TaskOrchestrationContext> context = new();
        Mock<TaskOrchestrationEntityFeature> entities = new();

        context.Setup(c => c.Entities).Returns(entities.Object);
        context.Setup(c => c.CreateReplaySafeLogger<BlobPurgeJobOrchestrator>()).Returns(this.logger);
        context.Setup(c => c.CreateTimer(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        entities
            .Setup(e => e.CallEntityAsync<BlobPurgeJobState?>(
                It.IsAny<EntityInstanceId>(),
                nameof(BlobPurgeJob.Get),
                It.IsAny<object?>(),
                It.IsAny<CallEntityOptions?>()))
            .ReturnsAsync(new BlobPurgeJobState { Status = BlobPurgeJobStatus.Active, PurgeBatchSize = 250 });

        entities
            .Setup(e => e.CallEntityAsync(
                It.IsAny<EntityInstanceId>(),
                It.IsAny<string>(),
                It.IsAny<object?>(),
                It.IsAny<CallEntityOptions?>()))
            .Returns(Task.CompletedTask);

        context
            .Setup(c => c.CallActivityAsync<List<LargePayloadTombstone>>(
                It.IsAny<TaskName>(), It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .ThrowsAsync(new TaskFailedException(
                nameof(GetLargePayloadTombstonesActivity),
                1,
                new NotImplementedException("backend does not implement GetLargePayloadTombstones")));

        // Act
        object? result = await new BlobPurgeJobOrchestrator().RunAsync(
            context.Object, new BlobPurgeJobRunRequest(JobEntityId, PurgeBatchSize: 100));

        // Assert - disabled through an AWAITED MarkUnsupported so the write is durable before the loop exits, a
        // dedicated diagnostic is logged (not the generic cycle-failed one), and RunAsync returns rather than
        // continuing. The awaited call, not a signal, is what guarantees the disable is committed here.
        result.Should().BeNull();
        entities.Verify(
            e => e.CallEntityAsync(
                JobEntityId,
                nameof(BlobPurgeJob.MarkUnsupported),
                It.IsAny<object?>(),
                It.IsAny<CallEntityOptions?>()),
            Times.Once);
        this.logger.Logs.Should().Contain(
            entry => entry.Message.Contains("does not implement the large-payload purge RPCs"));
        this.AssertNoCycleFailed();
    }

    [Fact]
    public async Task RunAsync_DeletesLargeBatch_InChunksNotOneActivityPerToken()
    {
        // Arrange - a full 1000-row batch. The whole point of chunking is that a batch this size fans out to a
        // HANDFUL of delete-activity calls (ceil(1000 / 50) = 20), not one per token (1000), which is what
        // bloated the orchestration history. The activity contract is one outcome per token, so the mock returns
        // exactly that; the orchestrator asserts the count before zipping.
        const int batchSize = 1000;
        List<LargePayloadTombstone> tombstones = [];
        for (int i = 0; i < batchSize; i++)
        {
            tombstones.Add(new LargePayloadTombstone(
                PartitionId: 1,
                InstanceKey: i,
                PayloadId: i,
                Token: $"blob:v2:https://acct.blob.core.windows.net/c/{i}",
                Revision: 1));
        }

        Mock<TaskOrchestrationContext> context = new();
        Mock<TaskOrchestrationEntityFeature> entities = new();

        context.Setup(c => c.Entities).Returns(entities.Object);
        context.Setup(c => c.CreateReplaySafeLogger<BlobPurgeJobOrchestrator>()).Returns(this.logger);
        context.Setup(c => c.CreateTimer(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Active on the first read, stopped on the second, so the perpetual loop runs exactly one cycle.
        entities
            .SetupSequence(e => e.CallEntityAsync<BlobPurgeJobState?>(
                It.IsAny<EntityInstanceId>(),
                nameof(BlobPurgeJob.Get),
                It.IsAny<object?>(),
                It.IsAny<CallEntityOptions?>()))
            .ReturnsAsync(new BlobPurgeJobState { Status = BlobPurgeJobStatus.Active, PurgeBatchSize = batchSize })
            .ReturnsAsync(new BlobPurgeJobState { Status = BlobPurgeJobStatus.Pending });

        entities
            .Setup(e => e.CallEntityAsync(
                It.IsAny<EntityInstanceId>(),
                It.IsAny<string>(),
                It.IsAny<object?>(),
                It.IsAny<CallEntityOptions?>()))
            .Returns(Task.CompletedTask);

        context
            .Setup(c => c.CallActivityAsync<List<LargePayloadTombstone>>(
                It.Is<TaskName>(n => n.Name == nameof(GetLargePayloadTombstonesActivity)),
                It.IsAny<object?>(),
                It.IsAny<TaskOptions?>()))
            .ReturnsAsync(tombstones);

        int chunkActivityCalls = 0;
        int tokensDeleted = 0;
        context
            .Setup(c => c.CallActivityAsync<List<BlobPurgeOutcome>>(
                It.Is<TaskName>(n => n.Name == nameof(DeleteExternalBlobActivity)),
                It.IsAny<object?>(),
                It.IsAny<TaskOptions?>()))
            .Returns<TaskName, object?, TaskOptions?>((_, input, _) =>
            {
                List<string> chunk = (List<string>)input!;
                Interlocked.Increment(ref chunkActivityCalls);
                Interlocked.Add(ref tokensDeleted, chunk.Count);

                // One outcome per token, positionally aligned - the shape the orchestrator asserts before zipping.
                List<BlobPurgeOutcome> outcomes = new(chunk.Count);
                foreach (string _ in chunk)
                {
                    outcomes.Add(new BlobPurgeOutcome(LargePayloadPurgeDisposition.Deleted));
                }

                return Task.FromResult(outcomes);
            });

        List<LargePayloadPurgeResult>? reported = null;
        context
            .Setup(c => c.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == nameof(ReportLargePayloadPurgeResultsActivity)),
                It.IsAny<object?>(),
                It.IsAny<TaskOptions?>()))
            .Callback<TaskName, object?, TaskOptions?>((_, input, _) => reported = (List<LargePayloadPurgeResult>)input!)
            .Returns(Task.CompletedTask);

        // Act
        await new BlobPurgeJobOrchestrator().RunAsync(
            context.Object, new BlobPurgeJobRunRequest(JobEntityId, PurgeBatchSize: batchSize));

        // Assert - 20 chunk activities for the whole batch, not 1000; every token was still handled exactly once;
        // and one result is reported per row so the backend hears about all 1000.
        this.AssertNoCycleFailed();
        chunkActivityCalls.Should().Be(20);
        tokensDeleted.Should().Be(batchSize);
        reported.Should().NotBeNull();
        reported!.Should().HaveCount(batchSize);
    }

    /// <summary>
    /// Guards against the whole test passing through the orchestrator's catch-all cycle handler, which would
    /// leave every observation empty and make the assertions vacuous.
    /// </summary>
    void AssertNoCycleFailed() =>
        this.logger.Logs.Should().NotContain(entry => entry.Message.Contains("cycle for job"));

    /// <summary>
    /// Builds a context whose first entity read returns <paramref name="first"/> and whose second returns a
    /// stopped job, so the perpetual loop runs at most one cycle and then exits. Batch sizes passed to the
    /// fetch activity are recorded.
    /// </summary>
    Mock<TaskOrchestrationContext> ContextFor(BlobPurgeJobState first)
    {
        Mock<TaskOrchestrationContext> context = new();
        Mock<TaskOrchestrationEntityFeature> entities = new();

        context.Setup(c => c.Entities).Returns(entities.Object);
        context.Setup(c => c.CreateReplaySafeLogger<BlobPurgeJobOrchestrator>()).Returns(this.logger);
        context.Setup(c => c.CreateTimer(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        entities
            .SetupSequence(e => e.CallEntityAsync<BlobPurgeJobState?>(
                It.IsAny<EntityInstanceId>(),
                nameof(BlobPurgeJob.Get),
                It.IsAny<object?>(),
                It.IsAny<CallEntityOptions?>()))
            .ReturnsAsync(first)
            .ReturnsAsync(new BlobPurgeJobState { Status = BlobPurgeJobStatus.Pending });

        context
            .Setup(c => c.CallActivityAsync<List<LargePayloadTombstone>>(
                It.IsAny<TaskName>(), It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Callback<TaskName, object?, TaskOptions?>((_, input, _) => this.requested.Add((int)input!))
            .ReturnsAsync([]);

        return context;
    }
}
