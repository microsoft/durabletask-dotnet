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
