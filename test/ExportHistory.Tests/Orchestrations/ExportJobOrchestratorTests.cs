// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Microsoft.DurableTask.ExportHistory.Tests.Orchestrations;

public class ExportJobOrchestratorTests
{
    static readonly DateTimeOffset PollTime = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Deserialize_LegacyRunRequest_PreservesReplayBehavior()
    {
        // Arrange
        EntityInstanceId entityId = new(nameof(ExportJob), "test-job");
        string? json = JsonDataConverter.Default.Serialize(new { JobEntityId = entityId, ProcessedCycles = 2 });

        // Act
        ExportJobRunRequest request = JsonDataConverter.Default.Deserialize<ExportJobRunRequest>(json)!;

        // Assert
        Assert.Equal(entityId, request.JobEntityId);
        Assert.Equal(2, request.ProcessedCycles);
        Assert.False(request.UseCompletionTimeWindows);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_OutOfOrderCompletion_ExportsEarlierCreatedInstance(bool legacyCheckpoint)
    {
        // Arrange
        TestRunner runner = new();
        runner.State.Config = runner.State.Config! with { MaxInstancesPerBatch = 1 };
        runner.State.Checkpoint = legacyCheckpoint ? new ExportCheckpoint("2") : null;
        Mock<DurableTaskClient> client = new("test-client");
        client.Setup(c => c.ListInstanceIdsAsync(
                It.IsAny<IEnumerable<OrchestrationRuntimeStatus>?>(),
                It.IsAny<DateTimeOffset?>(),
                It.IsAny<DateTimeOffset?>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns((
                IEnumerable<OrchestrationRuntimeStatus>? statuses,
                DateTimeOffset? from,
                DateTimeOffset? to,
                int pageSize,
                string? key,
                CancellationToken cancellation) =>
            {
                // A has an earlier creation key, but completes after B and after the first poll.
                (string Id, string Key, DateTimeOffset Completed)[] instances =
                [
                    ("A", "1", PollTime.AddSeconds(30)),
                    ("B", "2", PollTime.AddSeconds(-10)),
                ];
                var matches = instances
                    .Where(i => i.Completed <= runner.Now && i.Completed >= from && i.Completed <= to)
                    .Where(i => key == null || string.CompareOrdinal(i.Key, key) > 0)
                    .Take(pageSize)
                    .ToList();
                return Task.FromResult(new Page<string>(
                    matches.Select(i => i.Id).ToList(),
                    matches.Count == 0 ? key : matches[^1].Key));
            });
        ListTerminalInstancesActivity activity = new(client.Object, NullLogger<ListTerminalInstancesActivity>.Instance);
        runner.ListPage = request => activity.RunAsync(Mock.Of<TaskActivityContext>(), request);

        // Act
        await runner.RunAsync();

        // Assert
        Assert.Equal(new[] { "B", "A" }, runner.ExportedIds);
        Assert.Null(runner.ListRequests[0].LastInstanceKey);
        Assert.Equal(PollTime, runner.ListRequests[0].CompletedTimeTo);
        Assert.Equal("2", runner.ListRequests[1].LastInstanceKey);
        Assert.Equal(PollTime, runner.ListRequests[1].CompletedTimeTo);
        Assert.Null(runner.ListRequests[2].LastInstanceKey);
        Assert.Equal(PollTime, runner.ListRequests[2].CompletedTimeFrom);
        Assert.Equal(PollTime.AddMinutes(1), runner.ListRequests[2].CompletedTimeTo);
        Assert.Equal(2, runner.State.ExportedInstances);
    }

    [Fact]
    public async Task RunAsync_ContinueAsNew_ResumesSameWindowAndCursor()
    {
        // Arrange
        TestRunner runner = new();
        runner.ListPage = _ => Task.FromResult(new InstancePage(["instance"], new ExportCheckpoint("next-key")));

        // Act
        await runner.RunAsync(new ExportJobRunRequest(runner.EntityId, ProcessedCycles: 4)
        {
            UseCompletionTimeWindows = true,
        });
        ExportJobRunRequest continued = Assert.IsType<ExportJobRunRequest>(runner.ContinuedInput);
        runner.Now = PollTime.AddHours(1);
        runner.ListPage = _ =>
        {
            runner.State.Status = ExportJobStatus.Completed;
            return Task.FromResult(new InstancePage([], new ExportCheckpoint()));
        };
        await runner.RunAsync(continued);

        // Assert
        Assert.True(continued.UseCompletionTimeWindows);
        Assert.Equal("next-key", runner.ListRequests[1].LastInstanceKey);
        Assert.Equal(PollTime.AddHours(-1), runner.ListRequests[1].CompletedTimeFrom);
        Assert.Equal(PollTime, runner.ListRequests[1].CompletedTimeTo);
    }

    [Fact]
    public async Task RunAsync_InclusiveBoundary_IsNotSkippedBetweenWindows()
    {
        // Arrange
        TestRunner runner = new();
        runner.ListPage = request =>
        {
            if (runner.ListRequests.Count == 3)
            {
                runner.State.Status = ExportJobStatus.Completed;
            }

            List<string> ids = request.CompletedTimeFrom <= PollTime && request.CompletedTimeTo >= PollTime
                ? ["boundary-instance"]
                : [];
            return Task.FromResult(new InstancePage(ids, new ExportCheckpoint()));
        };

        // Act
        await runner.RunAsync();

        // Assert
        Assert.Equal(new[] { "boundary-instance", "boundary-instance" }, runner.ExportedIds);
        Assert.Equal(PollTime, runner.ListRequests[1].CompletedTimeFrom);
        Assert.Equal(PollTime.AddMinutes(1), runner.ListRequests[2].CompletedTimeFrom);
        Assert.All(runner.ListRequests, request => Assert.Null(request.LastInstanceKey));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("previous-key")]
    public async Task RunAsync_EmptyFinalPage_AdvancesWatermarkAndResetsCursor(string? nextKey)
    {
        // Arrange
        TestRunner runner = new();
        runner.State.Checkpoint = new ExportCheckpoint("previous-key")
        {
            CompletedTimeFrom = PollTime.AddHours(-1),
            CompletedTimeTo = PollTime,
        };
        runner.ListPage = _ =>
        {
            runner.State.Status = ExportJobStatus.Completed;
            return Task.FromResult(new InstancePage([], new ExportCheckpoint(nextKey)));
        };

        // Act
        await runner.RunAsync();

        // Assert
        Assert.Equal(new ExportCheckpoint { CompletedTimeFrom = PollTime }, runner.State.Checkpoint);
        Assert.Equal(1, runner.TimerCount);
        Assert.Empty(runner.ExportedIds);
        Assert.Equal(0, runner.State.ExportedInstances);
    }

    [Fact]
    public async Task RunAsync_EmptyPageWithAdvancingCursor_KeepsPagingSameWindow()
    {
        // Arrange
        TestRunner runner = new();
        runner.ListPage = _ =>
        {
            if (runner.ListRequests.Count == 1)
            {
                return Task.FromResult(new InstancePage([], new ExportCheckpoint("scanned-key")));
            }

            runner.State.Status = ExportJobStatus.Completed;
            return Task.FromResult(new InstancePage(["instance"], new ExportCheckpoint()));
        };

        // Act
        await runner.RunAsync();

        // Assert
        Assert.Equal("scanned-key", runner.ListRequests[1].LastInstanceKey);
        Assert.Equal(PollTime, runner.ListRequests[1].CompletedTimeTo);
        Assert.Equal(1, runner.TimerCount);
        Assert.Single(runner.ExportedIds);
    }

    [Fact]
    public async Task RunAsync_FailedExport_DoesNotAdvanceWindowOrCursor()
    {
        // Arrange
        TestRunner runner = new();
        ExportCheckpoint checkpoint = new("previous-key")
        {
            CompletedTimeFrom = PollTime.AddHours(-1),
            CompletedTimeTo = PollTime,
        };
        runner.State.Checkpoint = checkpoint;
        runner.ExportSucceeds = false;
        runner.ListPage = _ => Task.FromResult(new InstancePage(["instance"], new ExportCheckpoint()));

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync());

        // Assert
        Assert.Equal(checkpoint, runner.State.Checkpoint);
        Assert.Equal(0, runner.State.ExportedInstances);
        Assert.Equal(ExportJobStatus.Failed, runner.State.Status);
    }

    [Fact]
    public async Task RunAsync_FutureLowerBound_WaitsWithoutListing()
    {
        // Arrange
        TestRunner runner = new();
        runner.State.Config = runner.State.Config! with
        {
            Filter = new ExportFilter(PollTime.AddDays(1), null, null),
        };

        // Act
        await runner.RunAsync();

        // Assert
        Assert.Empty(runner.ListRequests);
        Assert.Equal(5, runner.TimerCount);
        Assert.Null(runner.State.Checkpoint);
    }

    [Theory]
    [InlineData(ExportMode.Continuous, false)]
    [InlineData(ExportMode.Batch, true)]
    public async Task RunAsync_LegacyHistoryOrBatch_PreservesListingBehavior(ExportMode mode, bool useWindows)
    {
        // Arrange
        TestRunner runner = new();
        runner.State.Config = runner.State.Config! with
        {
            Mode = mode,
            Filter = new ExportFilter(PollTime.AddHours(-1), mode == ExportMode.Batch ? PollTime : null, null),
        };
        runner.State.Checkpoint = new ExportCheckpoint("legacy-key");

        // Act
        await runner.RunAsync(new ExportJobRunRequest(runner.EntityId) { UseCompletionTimeWindows = useWindows });

        // Assert
        Assert.All(runner.ListRequests, request =>
        {
            Assert.Equal("legacy-key", request.LastInstanceKey);
            Assert.Equal(runner.State.Config.Filter.CompletedTimeTo, request.CompletedTimeTo);
        });
        if (mode == ExportMode.Continuous)
        {
            Assert.True(Assert.IsType<ExportJobRunRequest>(runner.ContinuedInput).UseCompletionTimeWindows);
        }
        else
        {
            Assert.Equal(ExportJobStatus.Completed, runner.State.Status);
            Assert.Equal(0, runner.TimerCount);
        }
    }

    sealed class TestRunner
    {
        readonly Mock<TaskOrchestrationContext> context = new(MockBehavior.Strict);
        readonly Mock<TaskOrchestrationEntityFeature> entities = new(MockBehavior.Strict);

        public TestRunner()
        {
            this.context.Setup(c => c.Entities).Returns(this.entities.Object);
            this.context.Setup(c => c.CurrentUtcDateTime).Returns(() => this.Now.UtcDateTime);
            this.context.Setup(c => c.CreateReplaySafeLogger<ExportJobOrchestrator>()).Returns(NullLogger.Instance);
            this.context.Setup(c => c.CreateTimer(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .Returns((TimeSpan delay, CancellationToken cancellation) =>
                {
                    this.Now += delay;
                    this.TimerCount++;
                    return Task.CompletedTask;
                });
            this.context.Setup(c => c.ContinueAsNew(It.IsAny<object?>(), true))
                .Callback((object? input, bool preserveEvents) => this.ContinuedInput = input);
            this.entities.Setup(e => e.CallEntityAsync<ExportJobState?>(
                    this.EntityId, nameof(ExportJob.Get), null, null))
                .ReturnsAsync(() => this.State);
            this.entities.Setup(e => e.CallEntityAsync<ExportJobState?>(
                    this.EntityId, nameof(ExportJob.Get), null))
                .CallBase();
            this.entities.Setup(e => e.CallEntityAsync(
                    this.EntityId, nameof(ExportJob.CommitCheckpoint), It.IsAny<object?>(), null))
                .Returns((EntityInstanceId id, string operation, object? input, CallEntityOptions? options) =>
                {
                    CommitCheckpointRequest request = Assert.IsType<CommitCheckpointRequest>(input);
                    if (request.Checkpoint != null)
                    {
                        this.State.Checkpoint = JsonDataConverter.Default.Deserialize<ExportCheckpoint>(
                            JsonDataConverter.Default.Serialize(request.Checkpoint));
                    }

                    this.State.ExportedInstances += request.ExportedInstances;
                    return Task.CompletedTask;
                });
            this.entities.Setup(e => e.CallEntityAsync(
                    this.EntityId, nameof(ExportJob.MarkAsCompleted), null, null))
                .Returns(() =>
                {
                    this.State.Status = ExportJobStatus.Completed;
                    return Task.CompletedTask;
                });
            this.entities.Setup(e => e.CallEntityAsync(
                    this.EntityId, nameof(ExportJob.MarkAsCompleted), null))
                .CallBase();
            this.entities.Setup(e => e.CallEntityAsync(
                    this.EntityId, nameof(ExportJob.MarkAsFailed), It.IsAny<object?>(), null))
                .Returns(() =>
                {
                    this.State.Status = ExportJobStatus.Failed;
                    return Task.CompletedTask;
                });
            this.context.Setup(c => c.CallActivityAsync<InstancePage>(
                    nameof(ListTerminalInstancesActivity), It.IsAny<object?>(), null))
                .Returns((TaskName name, object? input, TaskOptions? options) =>
                {
                    ListTerminalInstancesRequest request = Assert.IsType<ListTerminalInstancesRequest>(input);
                    this.ListRequests.Add(request);
                    return this.ListPage(request);
                });
            this.context.Setup(c => c.CallActivityAsync<ExportResult>(
                    nameof(ExportInstanceHistoryActivity), It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
                .Returns((TaskName name, object? input, TaskOptions? options) =>
                {
                    ExportRequest request = Assert.IsType<ExportRequest>(input);
                    this.ExportedIds.Add(request.InstanceId);
                    return Task.FromResult(new ExportResult
                    {
                        InstanceId = request.InstanceId,
                        Success = this.ExportSucceeds,
                        Error = this.ExportSucceeds ? null : "Export failed",
                    });
                });
        }

        public EntityInstanceId EntityId { get; } = new(nameof(ExportJob), "test-job");

        public ExportJobState State { get; } = new()
        {
            Status = ExportJobStatus.Active,
            Config = new ExportJobConfiguration(
                ExportMode.Continuous,
                new ExportFilter(PollTime.AddHours(-1), null, null),
                new ExportDestination("container"),
                ExportFormat.Default),
        };

        public DateTimeOffset Now { get; set; } = PollTime;

        public Func<ListTerminalInstancesRequest, Task<InstancePage>> ListPage { get; set; } =
            _ => Task.FromResult(new InstancePage([], new ExportCheckpoint()));

        public List<ListTerminalInstancesRequest> ListRequests { get; } = [];

        public List<string> ExportedIds { get; } = [];

        public bool ExportSucceeds { get; set; } = true;

        public int TimerCount { get; private set; }

        public object? ContinuedInput { get; private set; }

        public Task<object?> RunAsync(ExportJobRunRequest? input = null) =>
            new ExportJobOrchestrator().RunAsync(
                this.context.Object,
                input ?? new ExportJobRunRequest(this.EntityId) { UseCompletionTimeWindows = true });
    }
}
