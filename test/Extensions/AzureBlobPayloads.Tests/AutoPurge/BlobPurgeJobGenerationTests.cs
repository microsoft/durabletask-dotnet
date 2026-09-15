// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core.Entities;
using DurableTask.Core.Entities.OperationFormat;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Shims;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests.AutoPurge;

/// <summary>
/// Generation lifecycle regressions using actual entity serialization and orchestrator execution.
/// </summary>
public sealed class BlobPurgeJobGenerationTests
{
    /// <summary>
    /// Compares delivery after re-enable with the control that completes the old runner before re-enable.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisableEnable_UsesDifferentRunnerForReactivationAsync(
        bool deliverBeforeReenable)
    {
        // Arrange - both initial activation and its emitted Run signal execute through the real SDK shim.
        EntityDriver entity = new();
        EntityBatchResult initialCreate = await entity.ExecuteAsync(nameof(BlobPurgeJob.Create), 250);
        SendSignalOperationAction initialSignal =
            Assert.IsType<SendSignalOperationAction>(Assert.Single(initialCreate.Actions!));
        Assert.Equal(nameof(BlobPurgeJob.Run), initialSignal.Name);
        EntityBatchResult initialRun = await entity.ExecuteAsync(Assert.IsType<string>(initialSignal.Name));
        StartNewOrchestrationOperationAction oldStart =
            Assert.IsType<StartNewOrchestrationOperationAction>(Assert.Single(initialRun.Actions!));
        Assert.Equal(BlobPurgeJobStatus.Active, entity.State.Status);
        string firstGeneration = Assert.IsType<string>(entity.State.Generation);
        Assert.True(Guid.TryParseExact(firstGeneration, "N", out _));
        string runnerId = BlobPurgeConstants.GetOrchestratorInstanceId(BlobPurgeConstants.JobId, firstGeneration);
        Assert.Equal(runnerId, oldStart.InstanceId);

        TaskCompletionSource<BlobPurgeJobState?> getResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<TaskOrchestrationContext> context = new(MockBehavior.Strict);
        Mock<TaskOrchestrationEntityFeature> entityCalls = new(MockBehavior.Strict);
        TestLogger<BlobPurgeJobOrchestrator> logger = new();
        context.Setup(c => c.Entities).Returns(entityCalls.Object);
        context.Setup(c => c.CreateReplaySafeLogger<BlobPurgeJobOrchestrator>()).Returns(logger);
        entityCalls.Setup(e => e.CallEntityAsync<BlobPurgeJobState?>(
            EntityDriver.Id, nameof(BlobPurgeJob.Get), null, null)).Returns(getResponse.Task);
        Task<object?> oldRunner = new BlobPurgeJobOrchestrator().RunAsync(
            context.Object, entity.Converter.Deserialize<BlobPurgeJobRunRequest>(oldStart.Input)!);
        Assert.False(oldRunner.IsCompleted);
        entityCalls.Verify(e => e.CallEntityAsync<BlobPurgeJobState?>(
            EntityDriver.Id, nameof(BlobPurgeJob.Get), null, null), Times.Once);

        // Act - Stop then Get commit a real serialized response which is held independently of entity state.
        EntityBatchResult stopped = await entity.ExecuteAsync(nameof(BlobPurgeJob.Stop));
        Assert.Empty(stopped.Actions!);
        Assert.Equal(BlobPurgeJobStatus.Pending, entity.State.Status);
        EntityBatchResult get = await entity.ExecuteAsync(nameof(BlobPurgeJob.Get));
        Assert.Empty(get.Actions!);
        string capturedPending = Assert.IsType<string>(Assert.Single(get.Results!).Result);
        Assert.Equal(BlobPurgeJobStatus.Pending, entity.Converter.Deserialize<BlobPurgeJobState>(capturedPending)!.Status);
        Assert.False(oldRunner.IsCompleted);
        if (deliverBeforeReenable)
        {
            getResponse.SetResult(entity.Converter.Deserialize<BlobPurgeJobState>(capturedPending));
            Assert.Null(await oldRunner.WaitAsync(TimeSpan.FromSeconds(5)));
        }

        EntityBatchResult create = await entity.ExecuteAsync(nameof(BlobPurgeJob.Create), 500);
        SendSignalOperationAction signal = Assert.IsType<SendSignalOperationAction>(Assert.Single(create.Actions!));
        Assert.Equal(EntityDriver.Id.ToString(), signal.InstanceId);
        Assert.Equal(nameof(BlobPurgeJob.Run), signal.Name);
        EntityBatchResult run = await entity.ExecuteAsync(Assert.IsType<string>(signal.Name));
        StartNewOrchestrationOperationAction replacement =
            Assert.IsType<StartNewOrchestrationOperationAction>(Assert.Single(run.Actions!));
        bool oldRunningAtReplacementStart = !oldRunner.IsCompleted;
        Assert.Equal(!deliverBeforeReenable, oldRunningAtReplacementStart);
        Assert.NotEqual(oldStart.InstanceId, replacement.InstanceId);
        Assert.Equal(nameof(BlobPurgeJobOrchestrator), replacement.Name);
        Assert.Equal(BlobPurgeJobStatus.Active, entity.State.Status);
        BlobPurgeJobRunRequest newInput = entity.Converter.Deserialize<BlobPurgeJobRunRequest>(replacement.Input)!;
        Assert.Equal(entity.State.Generation, newInput.Generation);
        Assert.NotEqual(firstGeneration, newInput.Generation);
        Assert.Equal(500, entity.State.PurgeBatchSize);
        string persistedActive = Assert.IsType<string>(run.EntityState);

        if (!deliverBeforeReenable)
        {
            // Deserialize only now, proving Create did not mutate the already-serialized Pending result.
            getResponse.SetResult(entity.Converter.Deserialize<BlobPurgeJobState>(capturedPending));
            Assert.Null(await oldRunner.WaitAsync(TimeSpan.FromSeconds(5)));
        }

        // Assert - the old orchestrator exits on its stale snapshot without activities, retries, or new starts.
        Assert.True(oldRunner.IsCompletedSuccessfully);
        Assert.Equal(BlobPurgeJobStatus.Pending, entity.Converter.Deserialize<BlobPurgeJobState>(capturedPending)!.Status);
        Assert.Equal(BlobPurgeJobStatus.Active, entity.State.Status);
        Assert.Equal(persistedActive, entity.SerializedState);
        Assert.Contains(logger.Logs, entry => entry.Message.Contains("stopping") && entry.Message.Contains("Pending"));
        context.Verify(c => c.Entities, Times.Once);
        context.Verify(c => c.CreateReplaySafeLogger<BlobPurgeJobOrchestrator>(), Times.Once);
        context.VerifyNoOtherCalls();
        entityCalls.VerifyNoOtherCalls();
        Assert.Single(run.Actions!);
        Assert.DoesNotContain(typeof(StartNewOrchestrationOperationAction).GetProperties(),
            property => property.Name.Contains("Reuse", StringComparison.Ordinal));

        Mock<TaskOrchestrationContext> newRunner = await RunWithSnapshotsAsync(
            newInput, entity.State, new BlobPurgeJobState { Status = BlobPurgeJobStatus.Pending, Generation = newInput.Generation });
        AssertCycleCount(newRunner, 1);
    }

    /// <summary>
    /// Repeated enables keep the activation; stop/start and deleted state allocate collision-resistant IDs.
    /// </summary>
    [Fact]
    public async Task Create_ReusesActiveGeneration_AndChangesItOnlyOnActivationAsync()
    {
        // Arrange
        EntityDriver entity = new();
        await entity.ExecuteAsync(nameof(BlobPurgeJob.Create), 250);
        await entity.ExecuteAsync(nameof(BlobPurgeJob.RecordPurged), 7L);
        BlobPurgeJobState first = entity.State;

        // Act
        await entity.ExecuteAsync(nameof(BlobPurgeJob.Create), 250);
        Assert.Equal(first.Generation, entity.State.Generation);
        Assert.Equal(first.LastModifiedAt, entity.State.LastModifiedAt);
        await entity.ExecuteAsync(nameof(BlobPurgeJob.Create), 500);
        Assert.Equal(first.Generation, entity.State.Generation);
        Assert.Equal(500, entity.State.PurgeBatchSize);
        HashSet<string> generations = [first.Generation!];
        for (int i = 0; i < 3; i++)
        {
            string? previousGeneration = entity.State.Generation;
            await entity.ExecuteAsync(nameof(BlobPurgeJob.Stop));
            Assert.Equal(previousGeneration, entity.State.Generation);
            await entity.ExecuteAsync(nameof(BlobPurgeJob.Create), 500);
            Assert.True(generations.Add(entity.State.Generation!));
        }

        EntityDriver recreated = new();
        await recreated.ExecuteAsync(nameof(BlobPurgeJob.Create), 500);

        // Assert
        Assert.DoesNotContain(recreated.State.Generation!, generations);
        Assert.Equal(first.CreatedAt, entity.State.CreatedAt);
        Assert.Equal(7, entity.State.PurgedCount);
    }

    /// <summary>
    /// Only matching, nonempty generations can begin a cycle.
    /// </summary>
    [Theory]
    [InlineData("old", "new", 0)]
    [InlineData("new", null, 0)]
    [InlineData("new", "", 0)]
    [InlineData("new", "new", 1)]
    public async Task RunAsync_RequiresMatchingActiveGenerationAsync(string inputGeneration, string? stateGeneration, int cycles)
    {
        // Arrange
        BlobPurgeJobRunRequest input = new(EntityDriver.Id, 250, inputGeneration);

        // Act
        Mock<TaskOrchestrationContext> context = await RunWithSnapshotsAsync(input,
            new BlobPurgeJobState { Status = BlobPurgeJobStatus.Active, Generation = stateGeneration, PurgeBatchSize = 250 },
            new BlobPurgeJobState { Status = BlobPurgeJobStatus.Pending, Generation = stateGeneration });

        // Assert
        AssertCycleCount(context, cycles);
    }

    /// <summary>
    /// A previously authorized cycle may finish, but the next current-state read retires its old generation.
    /// </summary>
    [Fact]
    public async Task StaleActiveSnapshot_FinishesOneBatch_ThenRetiresAsync()
    {
        // Arrange
        EntityDriver entity = new();
        await entity.ExecuteAsync(nameof(BlobPurgeJob.Create), 250);
        EntityBatchResult get = await entity.ExecuteAsync(nameof(BlobPurgeJob.Get));
        string activeSnapshot = Assert.IsType<string>(Assert.Single(get.Results!).Result);
        BlobPurgeJobState first = entity.Converter.Deserialize<BlobPurgeJobState>(activeSnapshot)!;
        BlobPurgeJobRunRequest input = new(EntityDriver.Id, 250, first.Generation!);
        await entity.ExecuteAsync(nameof(BlobPurgeJob.Stop));
        await entity.ExecuteAsync(nameof(BlobPurgeJob.Create), 500);

        // Act
        Mock<TaskOrchestrationContext> context = await RunWithSnapshotsAsync(input, first, entity.State);
        await entity.ExecuteAsync(nameof(BlobPurgeJob.RecordPurged), 1L);

        // Assert
        AssertCycleCount(context, 1);
        Assert.Equal(BlobPurgeJobStatus.Active, entity.State.Status);
        Assert.NotEqual(first.Generation, entity.State.Generation);
        Assert.Equal(1, entity.State.PurgedCount);
    }

    /// <summary>
    /// Stale control callbacks cannot change a newer activation, while current callbacks still work.
    /// </summary>
    [Fact]
    public async Task UnsupportedCallbacks_AreGenerationFencedAsync()
    {
        // Arrange
        EntityDriver entity = new();
        await entity.ExecuteAsync(nameof(BlobPurgeJob.Create), 250);
        string first = entity.State.Generation!;
        await entity.ExecuteAsync(nameof(BlobPurgeJob.Stop));
        await entity.ExecuteAsync(nameof(BlobPurgeJob.Create), 250);
        string second = entity.State.Generation!;
        string active = entity.SerializedState!;

        // Act
        await entity.ExecuteAsync(nameof(BlobPurgeJob.MarkGenerationUnsupported),
            new BlobPurgeUnsupportedRequest(first, "stale"));
        Assert.Equal(active, entity.SerializedState);
        await entity.ExecuteAsync(nameof(BlobPurgeJob.MarkGenerationUnsupported),
            new BlobPurgeUnsupportedRequest(second, "current"));
        string unsupported = entity.SerializedState!;
        await entity.ExecuteAsync(nameof(BlobPurgeJob.MarkGenerationUnsupported),
            new BlobPurgeUnsupportedRequest(second, "duplicate"));

        // Assert
        Assert.Equal(unsupported, entity.SerializedState);
        Assert.Equal(BlobPurgeJobStatus.Unsupported, entity.State.Status);
        Assert.Equal("current", entity.State.LastError);
        await entity.ExecuteAsync(nameof(BlobPurgeJob.Create), 250);
        Assert.NotEqual(second, entity.State.Generation);
        Assert.Null(entity.State.LastError);
        await entity.ExecuteAsync(nameof(BlobPurgeJob.Stop));
        string stopped = entity.SerializedState!;
        await entity.ExecuteAsync(nameof(BlobPurgeJob.MarkGenerationUnsupported),
            new BlobPurgeUnsupportedRequest(entity.State.Generation!, "late after stop"));
        Assert.Equal(stopped, entity.SerializedState);
    }

    /// <summary>
    /// Continue-as-new preserves the activation's generation.
    /// </summary>
    [Fact]
    public async Task ContinueAsNew_PreservesGenerationAsync()
    {
        // Arrange
        BlobPurgeJobRunRequest input = new(EntityDriver.Id, 250, "generation", 5);
        Mock<TaskOrchestrationContext> context = new(MockBehavior.Strict);
        context.Setup(c => c.CreateReplaySafeLogger<BlobPurgeJobOrchestrator>())
            .Returns(new TestLogger<BlobPurgeJobOrchestrator>());
        BlobPurgeJobRunRequest? continued = null;
        context.Setup(c => c.ContinueAsNew(It.IsAny<object>(), It.IsAny<bool>()))
            .Callback<object, bool>((value, _) => continued = Assert.IsType<BlobPurgeJobRunRequest>(value));

        // Act
        Assert.Null(await new BlobPurgeJobOrchestrator().RunAsync(context.Object, input));

        // Assert
        Assert.NotNull(continued);
        Assert.Equal(input.Generation, continued.Generation);
        Assert.Equal(0, continued.ProcessedCycles);
        Assert.Equal(250, continued.PurgeBatchSize);
    }

    static async Task<Mock<TaskOrchestrationContext>> RunWithSnapshotsAsync(
        BlobPurgeJobRunRequest input, params BlobPurgeJobState?[] snapshots)
    {
        Mock<TaskOrchestrationContext> context = new(MockBehavior.Strict);
        Mock<TaskOrchestrationEntityFeature> entities = new(MockBehavior.Strict);
        Queue<BlobPurgeJobState?> reads = new(snapshots);
        context.Setup(c => c.Entities).Returns(entities.Object);
        context.Setup(c => c.CreateReplaySafeLogger<BlobPurgeJobOrchestrator>())
            .Returns(new TestLogger<BlobPurgeJobOrchestrator>());
        entities.Setup(e => e.CallEntityAsync<BlobPurgeJobState?>(EntityDriver.Id, nameof(BlobPurgeJob.Get), null, null))
            .ReturnsAsync(() => reads.Dequeue());
        entities.Setup(e => e.CallEntityAsync(EntityDriver.Id, nameof(BlobPurgeJob.RecordPurged), It.IsAny<object>(), null))
            .Returns(Task.CompletedTask);
        context.Setup(c => c.CallActivityAsync<List<LargePayloadTombstone>>(
            nameof(GetLargePayloadTombstonesActivity), It.IsAny<object>(), It.IsAny<TaskOptions>()))
            .ReturnsAsync([new LargePayloadTombstone("row", "blob:v2:https://test.invalid/c/blob")]);
        context.Setup(c => c.CallActivityAsync<List<BlobPurgeOutcome>>(
            nameof(DeleteExternalBlobActivity), It.IsAny<object>(), It.IsAny<TaskOptions>()))
            .ReturnsAsync([new BlobPurgeOutcome(LargePayloadPurgeDisposition.Deleted)]);
        context.Setup(c => c.CallActivityAsync(
            nameof(ReportLargePayloadPurgeResultsActivity), It.IsAny<object>(), It.IsAny<TaskOptions>()))
            .Returns(Task.CompletedTask);
        Assert.Null(await new BlobPurgeJobOrchestrator().RunAsync(context.Object, input).WaitAsync(TimeSpan.FromSeconds(5)));
        return context;
    }

    static void AssertCycleCount(Mock<TaskOrchestrationContext> context, int cycles)
    {
        context.Verify(c => c.CallActivityAsync<List<LargePayloadTombstone>>(
            nameof(GetLargePayloadTombstonesActivity), It.IsAny<object>(), It.IsAny<TaskOptions>()), Times.Exactly(cycles));
        context.Verify(c => c.CallActivityAsync<List<BlobPurgeOutcome>>(
            nameof(DeleteExternalBlobActivity), It.IsAny<object>(), It.IsAny<TaskOptions>()), Times.Exactly(cycles));
        context.Verify(c => c.CallActivityAsync(
            nameof(ReportLargePayloadPurgeResultsActivity), It.IsAny<object>(), It.IsAny<TaskOptions>()), Times.Exactly(cycles));
    }

    sealed class EntityDriver
    {
        public static readonly EntityInstanceId Id = new(nameof(BlobPurgeJob), BlobPurgeConstants.JobId);
        readonly DurableTaskShimFactory factory;

        public EntityDriver()
        {
            DurableTaskWorkerOptions options = new();
            this.Converter = options.DataConverter;
            this.factory = new DurableTaskShimFactory(options);
        }

        public DataConverter Converter { get; }

        public string? SerializedState { get; private set; }

        public BlobPurgeJobState State => this.Converter.Deserialize<BlobPurgeJobState>(this.SerializedState)!;

        public async Task<EntityBatchResult> ExecuteAsync(string operation, object? input = null)
        {
            global::DurableTask.Core.Entities.TaskEntity shim = this.factory.CreateEntity(
                nameof(BlobPurgeJob), new BlobPurgeJob(new TestLogger<BlobPurgeJob>()),
                new EntityId(nameof(BlobPurgeJob), BlobPurgeConstants.JobId));
            EntityBatchResult result = await shim.ExecuteOperationBatchAsync(new EntityBatchRequest
            {
                EntityState = this.SerializedState,
                Operations = [new OperationRequest { Operation = operation, Input = this.Converter.Serialize(input) }],
            });
            Assert.Null(result.FailureDetails);
            Assert.NotNull(result.Actions);
            Assert.NotNull(result.Results);
            Assert.Null(Assert.Single(result.Results).FailureDetails);
            this.SerializedState = result.EntityState;
            return result;
        }
    }
}
