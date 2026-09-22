// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.Command;
using DurableTask.Core.History;
using Grpc.Core;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Worker.Shims;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests.AutoPurge;

public class AlternateHostPurgeTaskTests
{
    [Fact]
    public async Task ActualTasks_ExecuteWithDtfXArguments_AndPreserveCorrelationAcrossReplayAsync()
    {
        // Arrange
        const string FirstToken = "opaque:row/one+==";
        const string SecondToken = "opaque:\"row two\"";
        List<LargePayloadTombstone> tombstones =
        [
            new(FirstToken, "blob:v2:https://account.blob.core.windows.net/payloads/one"),
            new(SecondToken, "blob:v2:https://account.blob.core.windows.net/payloads/two"),
        ];
        Mock<ILargePayloadPurgeClient> purge = new(MockBehavior.Strict);
        DateTime? fetchDeadline = null;
        DateTime? reportDeadline = null;
        purge.Setup(p => p.GetLargePayloadTombstonesAsync(37, It.IsAny<DateTime>(), default))
            .Callback<int, DateTime, CancellationToken>((_, deadline, _) => fetchDeadline = deadline)
            .ReturnsAsync(tombstones);
        List<LargePayloadPurgeResult>? reported = null;
        purge.Setup(p => p.ReportLargePayloadPurgeResultsAsync(It.IsAny<IReadOnlyList<LargePayloadPurgeResult>>(), It.IsAny<DateTime>(), default))
            .Callback<IReadOnlyList<LargePayloadPurgeResult>, DateTime, CancellationToken>((results, deadline, _) =>
            {
                reported = results.ToList();
                reportDeadline = deadline;
            }).Returns(Task.CompletedTask);
        Mock<PayloadStore> store = new(MockBehavior.Strict);
        store.Setup(s => s.DeleteAsync(tombstones[0].PayloadToken, It.IsAny<CancellationToken>())).ReturnsAsync(PayloadDeleteOutcome.Deleted);
        store.Setup(s => s.DeleteAsync(tombstones[1].PayloadToken, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());
        Driver driver = new(new(37));
        DateTime earliestDeadline = DateTime.UtcNow.AddSeconds(60);

        // Act
        ScheduleTaskOrchestratorAction fetch = driver.SingleActivity(nameof(GetLargePayloadTombstonesActivity));
        Assert.Equal("[37]", fetch.Input);
        await driver.RunActivityAsync(fetch, new GetLargePayloadTombstonesActivity(purge.Object, NullLogger<GetLargePayloadTombstonesActivity>.Instance));
        ScheduleTaskOrchestratorAction delete = driver.SingleActivity(nameof(DeleteExternalBlobActivity));
        Assert.StartsWith("[[", delete.Input);
        await driver.RunActivityAsync(delete, new DeleteExternalBlobActivity(store.Object, NullLogger<DeleteExternalBlobActivity>.Instance));
        ScheduleTaskOrchestratorAction report = driver.SingleActivity(nameof(ReportLargePayloadPurgeResultsActivity));
        Assert.StartsWith("[[", report.Input);
        await driver.RunActivityAsync(report, new ReportLargePayloadPurgeResultsActivity(purge.Object, NullLogger<ReportLargePayloadPurgeResultsActivity>.Instance));

        // Assert
        Assert.Equal(new[]
        {
            new LargePayloadPurgeResult(FirstToken, LargePayloadPurgeDisposition.Deleted),
            new LargePayloadPurgeResult(SecondToken, LargePayloadPurgeDisposition.Retry),
        }, reported);
        Assert.Contains("\"PurgedCount\":1", driver.Result.CustomStatus);
        Assert.Equal("[37]", driver.SingleActivity(nameof(GetLargePayloadTombstonesActivity)).Input);
        Assert.All(new[] { fetchDeadline, reportDeadline }, deadline =>
        {
            Assert.NotNull(deadline);
            Assert.Equal(DateTimeKind.Utc, deadline.Value.Kind);
            Assert.InRange(deadline.Value, earliestDeadline, DateTime.UtcNow.AddSeconds(60));
        });
        Assert.Equal(driver.Snapshot(), driver.ReplaySnapshot());
        store.VerifyAll();
        purge.VerifyAll();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsupportedActivity_FailureDetailsReachOrchestrator_AndEventResumesWithoutRetryAsync(bool reportFailure)
    {
        // Arrange
        Mock<ILargePayloadPurgeClient> purge = new(MockBehavior.Strict);
        RpcException unsupported = new(new Status(StatusCode.Unimplemented, "unsupported"));
        purge.Setup(p => p.GetLargePayloadTombstonesAsync(It.IsAny<int>(), It.IsAny<DateTime>(), default))
            .ThrowsAsync(unsupported);
        purge.Setup(p => p.ReportLargePayloadPurgeResultsAsync(It.IsAny<IReadOnlyList<LargePayloadPurgeResult>>(), It.IsAny<DateTime>(), default))
            .ThrowsAsync(unsupported);
        Driver driver = new(new(20));
        ScheduleTaskOrchestratorAction activity = driver.SingleActivity(nameof(GetLargePayloadTombstonesActivity));
        ITaskActivity implementation = new GetLargePayloadTombstonesActivity(purge.Object, NullLogger<GetLargePayloadTombstonesActivity>.Instance);
        if (reportFailure)
        {
            driver.Complete(activity, new[] { new LargePayloadTombstone("correlation", "blob:v2:payload") });
            driver.Complete(driver.SingleActivity(nameof(DeleteExternalBlobActivity)), new[] { new BlobPurgeOutcome(LargePayloadPurgeDisposition.Deleted) });
            activity = driver.SingleActivity(nameof(ReportLargePayloadPurgeResultsActivity));
            implementation = new ReportLargePayloadPurgeResultsActivity(purge.Object, NullLogger<ReportLargePayloadPurgeResultsActivity>.Instance);
        }

        // Act
        Exception? error = await Record.ExceptionAsync(() => driver.InvokeActivityAsync(activity, implementation));
        NotImplementedException failure = Assert.IsType<NotImplementedException>(error);
        driver.Fail(activity, failure);

        // Assert
        Assert.Same(unsupported, failure.InnerException);
        Assert.Empty(driver.Result.Actions);
        Assert.Contains("\"Status\":\"BackendUnsupported\"", driver.Result.CustomStatus);
        Assert.Equal(driver.Snapshot(), driver.ReplaySnapshot());
        driver.Turn(Driver.Configure(71));
        Assert.Equal("[71]", driver.SingleActivity(nameof(GetLargePayloadTombstonesActivity)).Input);
    }

    [Fact]
    public async Task TransientActivityFailure_KeepsDtfXRetryTimerAsync()
    {
        // Arrange
        Mock<ILargePayloadPurgeClient> purge = new(MockBehavior.Strict);
        RpcException unavailable = new(new Status(StatusCode.Unavailable, "unavailable"));
        purge.Setup(p => p.GetLargePayloadTombstonesAsync(It.IsAny<int>(), It.IsAny<DateTime>(), default)).ThrowsAsync(unavailable);
        Driver driver = new(new(20));
        ScheduleTaskOrchestratorAction fetch = driver.SingleActivity(nameof(GetLargePayloadTombstonesActivity));
        GetLargePayloadTombstonesActivity activity = new(purge.Object, NullLogger<GetLargePayloadTombstonesActivity>.Instance);

        // Act
        Exception? error = await Record.ExceptionAsync(() => driver.InvokeActivityAsync(fetch, activity));
        driver.Fail(fetch, Assert.IsType<RpcException>(error));

        // Assert
        CreateTimerOrchestratorAction retry = Assert.IsType<CreateTimerOrchestratorAction>(Assert.Single(driver.Result.Actions));
        Assert.Equal(driver.Now.AddSeconds(15), retry.FireAt);
        Assert.Equal(driver.Snapshot(), driver.ReplaySnapshot());
    }

    [Fact]
    public void ConfigurationAndContinueAsNew_PreserveBufferedEventsAndStateThroughDtfXReplay()
    {
        // Arrange
        Driver driver = new(new(100, 9));
        for (int i = 0; i < 5; i++)
        {
            driver.Complete(driver.SingleActivity(nameof(GetLargePayloadTombstonesActivity)), Array.Empty<LargePayloadTombstone>());
            CreateTimerOrchestratorAction timer = Assert.IsType<CreateTimerOrchestratorAction>(Assert.Single(driver.Result.Actions));
            if (i < 4)
            {
                driver.Turn(Driver.TimerFired(timer));
            }
        }

        // Act
        driver.Turn(Driver.Configure(700), Driver.Configure(800));
        OrchestrationCompleteOrchestratorAction completed = Assert.IsType<OrchestrationCompleteOrchestratorAction>(Assert.Single(driver.Result.Actions));

        // Assert
        Assert.Equal(OrchestrationStatus.ContinuedAsNew, completed.OrchestrationStatus);
        BlobPurgeJobRunRequest next = JsonDataConverter.Default.Deserialize<BlobPurgeJobRunRequest>(completed.Result)!;
        Assert.Equal(new BlobPurgeJobRunRequest(700, 9), next);
        EventRaisedEvent carried = Assert.IsType<EventRaisedEvent>(Assert.Single(completed.CarryoverEvents));
        Assert.Equal(BlobPurgeConstants.SetBatchSizeEvent, carried.Name);
        Assert.Equal("800", carried.Input);
        Assert.Equal(driver.Snapshot(), driver.ReplaySnapshot());
        Driver nextDriver = new(next, carried);
        nextDriver.Complete(nextDriver.SingleActivity(nameof(GetLargePayloadTombstonesActivity)), Array.Empty<LargePayloadTombstone>());
        Assert.Equal("[800]", nextDriver.SingleActivity(nameof(GetLargePayloadTombstonesActivity)).Input);
    }

    sealed class Driver
    {
        readonly DurableTaskShimFactory factory = new();
        readonly List<HistoryEvent> history = [];
        readonly OrchestrationInstance instance = new()
        {
            InstanceId = BlobPurgeConstants.OrchestratorInstanceId,
            ExecutionId = "alternate-host",
        };
        List<HistoryEvent> lastPast = [];
        List<HistoryEvent> lastNew = [];

        public Driver(BlobPurgeJobRunRequest input, params HistoryEvent[] events)
        {
            this.Turn([new ExecutionStartedEvent(-1, JsonDataConverter.Default.Serialize(input))
            {
                Name = nameof(BlobPurgeJobOrchestrator),
                Version = string.Empty,
                OrchestrationInstance = this.instance,
            }, .. events]);
        }

        public DateTime Now { get; private set; } = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        public OrchestratorExecutionResult Result { get; private set; } = null!;

        public static EventRaisedEvent Configure(int size) => new(-1, JsonDataConverter.Default.Serialize(size)) { Name = BlobPurgeConstants.SetBatchSizeEvent };
        public static TimerFiredEvent TimerFired(CreateTimerOrchestratorAction timer) => new(-1, timer.FireAt) { TimerId = timer.Id };

        public ScheduleTaskOrchestratorAction SingleActivity(string name)
        {
            ScheduleTaskOrchestratorAction task = Assert.IsType<ScheduleTaskOrchestratorAction>(Assert.Single(this.Result.Actions));
            Assert.Equal(name, task.Name);
            Assert.Equal(string.Empty, task.Version);
            return task;
        }

        public Task<string> InvokeActivityAsync(ScheduleTaskOrchestratorAction action, ITaskActivity implementation) =>
            this.factory.CreateActivity(Assert.IsType<string>(action.Name), implementation).RunAsync(
                new TaskContext(this.instance, action.Name, action.Version, action.Id), action.Input);

        public async Task RunActivityAsync(ScheduleTaskOrchestratorAction action, ITaskActivity implementation) =>
            this.Turn(new TaskCompletedEvent(-1, action.Id, await this.InvokeActivityAsync(action, implementation)));

        public void Complete(ScheduleTaskOrchestratorAction action, object result) =>
            this.Turn(new TaskCompletedEvent(-1, action.Id, JsonDataConverter.Default.Serialize(result)));

        public void Fail(ScheduleTaskOrchestratorAction action, Exception failure) =>
            this.Turn(new TaskFailedEvent(-1, action.Id, failure.Message, null, new FailureDetails(failure)));

        public string Snapshot() => Serialize(this.Result);
        public string ReplaySnapshot() => Serialize(this.Replay());

        public void Turn(params HistoryEvent[] events)
        {
            this.Now = events.OfType<TimerFiredEvent>().Select(e => e.FireAt).Append(this.Now.AddSeconds(1)).Max();
            this.lastPast = [.. this.history];
            this.lastNew = [new OrchestratorStartedEvent(-1) { Timestamp = this.Now }];
            foreach (HistoryEvent item in events)
            {
                item.Timestamp = this.Now;
                this.lastNew.Add(item);
            }

            this.Result = this.Replay();
            this.history.AddRange(this.lastNew);
            foreach (OrchestratorAction action in this.Result.Actions)
            {
                if (action is ScheduleTaskOrchestratorAction task)
                {
                    this.history.Add(new TaskScheduledEvent(task.Id, Assert.IsType<string>(task.Name), task.Version, task.Input) { Timestamp = this.Now });
                }
                else if (action is CreateTimerOrchestratorAction timer)
                {
                    this.history.Add(new TimerCreatedEvent(timer.Id, timer.FireAt) { Timestamp = this.Now });
                }
            }

            this.history.Add(new OrchestratorCompletedEvent(-1) { Timestamp = this.Now });
        }

        static string Serialize(OrchestratorExecutionResult result) =>
            Newtonsoft.Json.JsonConvert.SerializeObject(new { result.CustomStatus, Actions = result.Actions.ToArray() });

        OrchestratorExecutionResult Replay()
        {
            OrchestrationRuntimeState state = new(this.lastPast);
            foreach (HistoryEvent item in this.lastNew)
            {
                state.AddEvent(item);
            }

            TaskOrchestration task = this.factory.CreateOrchestration(nameof(BlobPurgeJobOrchestrator), new BlobPurgeJobOrchestrator());
            TaskOrchestrationExecutor executor = new(state, task, BehaviorOnContinueAsNew.Carryover, ErrorPropagationMode.UseFailureDetails);
            return executor.Execute();
        }
    }
}
