// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Worker.Grpc;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests.AutoPurge;

/// <summary>
/// Drives the production orchestrator through the real protobuf runner, shim and replay executor.
/// </summary>
public class BlobPurgeJobOrchestratorTests
{
    [Fact]
    public void EmptyFetch_WaitsDurably_AndConfigurationInterruptsTimer()
    {
        // Arrange
        Driver driver = new(new BlobPurgeJobRunRequest(100));
        P.OrchestratorAction fetch = driver.SingleActivity(nameof(GetLargePayloadTombstonesActivity));

        // Act
        driver.Complete(fetch, Array.Empty<LargePayloadTombstone>());
        P.OrchestratorAction timer = Assert.Single(driver.Response.Actions);

        // Assert
        Assert.NotNull(timer.CreateTimer);
        Assert.Equal(driver.Now.AddMinutes(1), timer.CreateTimer.FireAt.ToDateTime());
        Assert.Contains("\"Status\":\"Waiting\"", driver.Response.CustomStatus);
        driver.Turn(Driver.Configure(777));
        Assert.Equal("[777]", driver.SingleActivity(nameof(GetLargePayloadTombstonesActivity)).ScheduleTask.Input);
        Assert.DoesNotContain(driver.Response.Actions, a => a.CreateTimer is not null || a.CompleteOrchestration is not null);
    }

    [Fact]
    public void BufferedAndInFlightConfiguration_UsesLatestValueOnNextCycle()
    {
        // Arrange - events received after execution starts apply at the next cycle boundary.
        Driver driver = new(new BlobPurgeJobRunRequest(100), Driver.Configure(200), Driver.Configure(300));
        P.OrchestratorAction fetch = driver.SingleActivity(nameof(GetLargePayloadTombstonesActivity));
        Assert.Equal("[100]", fetch.ScheduleTask.Input);

        // Act - the first event completed the one waiter; later events are buffered before its replacement exists.
        driver.Turn(Driver.Configure(400), Driver.Configure(500));
        Assert.Empty(driver.Response.Actions);
        driver.Complete(fetch, Array.Empty<LargePayloadTombstone>());

        // Assert - cancellation does not leave an extra active idle timer or a competing configuration waiter.
        Assert.Equal("[500]", driver.SingleActivity(nameof(GetLargePayloadTombstonesActivity)).ScheduleTask.Input);
        driver.Turn(Driver.Configure(600));
        Assert.Empty(driver.Response.Actions);
    }

    [Fact]
    public void ConfigurationAtContinueAsNew_IsAppliedOrPreserved_AndReplays()
    {
        // Arrange
        Driver driver = new(new BlobPurgeJobRunRequest(100, PurgedCount: 9));
        driver.CompleteEmptyCycles(4);
        P.OrchestratorAction fetch = driver.SingleActivity(nameof(GetLargePayloadTombstonesActivity));
        driver.Complete(fetch, Array.Empty<LargePayloadTombstone>());
        P.OrchestratorAction timer = Assert.Single(driver.Response.Actions);

        // Act - first event is delivered to the live waiter; a later event in this turn must survive CAN too.
        driver.Turn(Driver.Configure(700), Driver.TimerFired(timer), Driver.Configure(800));
        P.CompleteOrchestrationAction completed = Assert.Single(
            driver.Response.Actions, a => a.CompleteOrchestration is not null).CompleteOrchestration;

        // Assert
        Assert.Equal(P.OrchestrationStatus.ContinuedAsNew, completed.OrchestrationStatus);
        BlobPurgeJobRunRequest next = JsonDataConverter.Default.Deserialize<BlobPurgeJobRunRequest>(completed.Result)!;
        Assert.Equal(700, next.PurgeBatchSize);
        Assert.DoesNotContain("ProcessedCycles", completed.Result);
        Assert.Equal(9, next.PurgedCount);
        P.HistoryEvent forwarded = Assert.Single(completed.CarryoverEvents);
        Assert.Equal(BlobPurgeConstants.SetBatchSizeEvent, forwarded.EventRaised.Name);
        Assert.Equal("800", forwarded.EventRaised.Input);
        Driver nextDriver = new(next, forwarded.Clone());
        P.OrchestratorAction nextFetch = nextDriver.SingleActivity(nameof(GetLargePayloadTombstonesActivity));
        nextDriver.Complete(nextFetch, Array.Empty<LargePayloadTombstone>());
        Assert.Equal("[800]", nextDriver.SingleActivity(nameof(GetLargePayloadTombstonesActivity)).ScheduleTask.Input);
        Assert.Equal(driver.Response.ToByteArray(), driver.ReplayLastTurn().ToByteArray());
    }

    [Fact]
    public void FiveEmptyCycles_ContinueAsNewWithoutLosingCurrentBatch()
    {
        // Arrange
        Driver driver = new(new BlobPurgeJobRunRequest(250));

        // Act
        driver.CompleteEmptyCycles(5);

        // Assert
        P.CompleteOrchestrationAction can = Assert.Single(driver.Response.Actions).CompleteOrchestration;
        Assert.Equal(P.OrchestrationStatus.ContinuedAsNew, can.OrchestrationStatus);
        BlobPurgeJobRunRequest input = JsonDataConverter.Default.Deserialize<BlobPurgeJobRunRequest>(can.Result)!;
        Assert.Equal(250, input.PurgeBatchSize);
        Assert.DoesNotContain("ProcessedCycles", can.Result);
    }

    [Fact]
    public void ConfigurationDeliveredDuringFinalFetch_IsCarriedBeforeCan()
    {
        // Arrange
        Driver driver = new(new BlobPurgeJobRunRequest(250));
        driver.CompleteEmptyCycles(4);
        P.OrchestratorAction fetch = driver.SingleActivity(nameof(GetLargePayloadTombstonesActivity));

        // Act
        driver.Turn(Driver.Configure(400), Driver.Configure(500));
        driver.Complete(fetch, Array.Empty<LargePayloadTombstone>());

        // Assert - both the delivered waiter result and buffered event are consumed before history rollover.
        P.CompleteOrchestrationAction can = Assert.Single(driver.Response.Actions).CompleteOrchestration;
        Assert.Equal(P.OrchestrationStatus.ContinuedAsNew, can.OrchestrationStatus);
        BlobPurgeJobRunRequest input = JsonDataConverter.Default.Deserialize<BlobPurgeJobRunRequest>(can.Result)!;
        Assert.Equal(500, input.PurgeBatchSize);
        Assert.Empty(can.CarryoverEvents);
    }

    [Fact]
    public void TransientActivityFailure_BacksOffAndRetriesWithoutCompleting()
    {
        // Arrange
        Driver driver = new(new BlobPurgeJobRunRequest(250));
        P.OrchestratorAction fetch = driver.SingleActivity(nameof(GetLargePayloadTombstonesActivity));

        // Act - after activity retry exhaustion, a transient error must not enter unsupported wait.
        driver.Turn(Driver.ActivityFailed(fetch, new TimeoutException("transient")));

        // Assert
        P.OrchestratorAction timer = Assert.Single(driver.Response.Actions);
        Assert.NotNull(timer.CreateTimer);
        Assert.Contains("Waiting", driver.Response.CustomStatus);
        driver.Turn(Driver.TimerFired(timer));
        driver.SingleActivity(nameof(GetLargePayloadTombstonesActivity));
        Assert.DoesNotContain(driver.Response.Actions, a => a.CompleteOrchestration is not null);
    }

    [Theory]
    [InlineData(LargePayloadPurgeDisposition.Deleted)]
    [InlineData(LargePayloadPurgeDisposition.Retry)]
    [InlineData(LargePayloadPurgeDisposition.Quarantined)]
    public void Fanout_PreservesChunkBoundAndOpaqueTokens(LargePayloadPurgeDisposition disposition)
    {
        // Arrange
        Driver driver = new(new BlobPurgeJobRunRequest(500, PurgedCount: 7));
        LargePayloadTombstone[] batch = Enumerable.Range(0, 500)
            .Select(i => new LargePayloadTombstone($"opaque-row-{i}", $"blob:v2:https://test.invalid/c/{i}")).ToArray();

        // Act
        driver.Complete(driver.SingleActivity(nameof(GetLargePayloadTombstonesActivity)), batch);
        int chunks = 0;
        List<string> storageTokens = [];
        while (driver.Response.Actions.Any(a => a.ScheduleTask?.Name == nameof(DeleteExternalBlobActivity)))
        {
            P.OrchestratorAction[] wave = driver.Response.Actions.ToArray();
            Assert.InRange(wave.Length, 1, 4);
            foreach (P.OrchestratorAction action in wave)
            {
                string[][] inputs = JsonDataConverter.Default.Deserialize<string[][]>(action.ScheduleTask.Input)!;
                Assert.Equal(50, inputs[0].Length);
                storageTokens.AddRange(inputs[0]);
            }
            chunks += wave.Length;
            driver.Turn(wave.Select(a => Driver.ActivityCompleted(a,
                Enumerable.Repeat(new BlobPurgeOutcome(disposition), 50).ToArray())).ToArray());
        }
        P.OrchestratorAction report = driver.SingleActivity(nameof(ReportLargePayloadPurgeResultsActivity));
        LargePayloadPurgeResult[][] reports = JsonDataConverter.Default.Deserialize<LargePayloadPurgeResult[][]>(report.ScheduleTask.Input)!;
        driver.Complete(report, null);

        // Assert
        Assert.Equal(10, chunks);
        Assert.Equal(batch.Select(t => t.PayloadToken), storageTokens);
        Assert.Equal(batch.Select(t => t.TombstoneToken), reports[0].Select(r => r.TombstoneToken));
        Assert.All(reports[0], r => Assert.Equal(disposition, r.Disposition));
        Assert.Contains($"\"PurgedCount\":{(disposition == LargePayloadPurgeDisposition.Deleted ? 507 : 7)}", driver.Response.CustomStatus);
        if (disposition == LargePayloadPurgeDisposition.Retry)
        {
            P.OrchestratorAction timer = Assert.Single(driver.Response.Actions);
            Assert.NotNull(timer.CreateTimer);
            Assert.Equal(driver.Now.AddMinutes(1), timer.CreateTimer.FireAt.ToDateTime());
            driver.Turn(Driver.Configure(400));
            Assert.Equal("[400]", driver.SingleActivity(nameof(GetLargePayloadTombstonesActivity)).ScheduleTask.Input);
            Assert.DoesNotContain(driver.Response.Actions, action => action.CreateTimer is not null);
        }
        else
        {
            driver.SingleActivity(nameof(GetLargePayloadTombstonesActivity));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void UnsupportedBackend_WaitsInCurrentExecution_UntilConfiguration(int previousCycles)
    {
        // Arrange
        Driver driver = new(new BlobPurgeJobRunRequest(250, PurgedCount: 9));
        driver.CompleteEmptyCycles(previousCycles);
        P.OrchestratorAction fetch = driver.SingleActivity(nameof(GetLargePayloadTombstonesActivity));

        // Act
        driver.Turn(Driver.ActivityFailed(fetch, new NotImplementedException("backend unsupported")));

        // Assert
        Assert.Empty(driver.Response.Actions);
        Assert.Equal("{\"Status\":\"BackendUnsupported\",\"BatchSize\":250,\"PurgedCount\":9}", driver.Response.CustomStatus);
        Assert.Equal(driver.Response.ToByteArray(), driver.ReplayLastTurn().ToByteArray());
        driver.Turn();
        Assert.Empty(driver.Response.Actions);
        Assert.Equal("{\"Status\":\"BackendUnsupported\",\"BatchSize\":250,\"PurgedCount\":9}", driver.Response.CustomStatus);
        driver.Turn(Driver.Configure(400));
        Assert.Equal(driver.Response.ToByteArray(), driver.ReplayLastTurn().ToByteArray());
        if (previousCycles == 0)
        {
            Assert.Equal("[400]", driver.SingleActivity(nameof(GetLargePayloadTombstonesActivity)).ScheduleTask.Input);
            Assert.Equal("{\"Status\":\"Running\",\"BatchSize\":400,\"PurgedCount\":9}", driver.Response.CustomStatus);
        }
        else
        {
            P.CompleteOrchestrationAction can = Assert.Single(driver.Response.Actions).CompleteOrchestration;
            Assert.Equal(P.OrchestrationStatus.ContinuedAsNew, can.OrchestrationStatus);
            Assert.Equal("{\"PurgeBatchSize\":400,\"PurgedCount\":9}", can.Result);
            BlobPurgeJobRunRequest next = JsonDataConverter.Default.Deserialize<BlobPurgeJobRunRequest>(can.Result)!;
            Driver continued = new(next);
            Assert.Equal("[400]", continued.SingleActivity(nameof(GetLargePayloadTombstonesActivity)).ScheduleTask.Input);
            Assert.Equal("{\"Status\":\"Running\",\"BatchSize\":400,\"PurgedCount\":9}", continued.Response.CustomStatus);
        }
    }

    [Fact]
    public void UnsupportedAtFinalCycle_WakeThenCan_PreservesLateConfiguration()
    {
        // Arrange
        Driver driver = new(new BlobPurgeJobRunRequest(250, PurgedCount: 7));
        driver.CompleteEmptyCycles(4);
        driver.Turn(Driver.ActivityFailed(driver.SingleActivity(nameof(GetLargePayloadTombstonesActivity)),
            new NotImplementedException("unsupported")));
        Assert.Empty(driver.Response.Actions);

        // Act - the first event releases the existing waiter; the second arrives after this execution calls CAN.
        driver.Turn(Driver.Configure(333), Driver.Configure(444));
        P.CompleteOrchestrationAction can = Assert.Single(driver.Response.Actions).CompleteOrchestration;

        // Assert
        Assert.Equal(P.OrchestrationStatus.ContinuedAsNew, can.OrchestrationStatus);
        Assert.Equal("{\"PurgeBatchSize\":333,\"PurgedCount\":7}", can.Result);
        P.HistoryEvent forwarded = Assert.Single(can.CarryoverEvents);
        Assert.Equal(BlobPurgeConstants.SetBatchSizeEvent, forwarded.EventRaised.Name);
        Assert.Equal("444", forwarded.EventRaised.Input);
        Assert.Equal(driver.Response.ToByteArray(), driver.ReplayLastTurn().ToByteArray());
        BlobPurgeJobRunRequest next = JsonDataConverter.Default.Deserialize<BlobPurgeJobRunRequest>(can.Result)!;
        Driver continued = new(next, forwarded.Clone());
        P.OrchestratorAction fetch = continued.SingleActivity(nameof(GetLargePayloadTombstonesActivity));
        Assert.Equal("[333]", fetch.ScheduleTask.Input);
        continued.Complete(fetch, Array.Empty<LargePayloadTombstone>());
        Assert.Equal("[444]", continued.SingleActivity(nameof(GetLargePayloadTombstonesActivity)).ScheduleTask.Input);
        Assert.Equal("{\"Status\":\"Running\",\"BatchSize\":444,\"PurgedCount\":7}", continued.Response.CustomStatus);
    }

    [Fact]
    public void UnsupportedWithDeliveredAndBufferedConfiguration_DrainsBeforeCan()
    {
        // Arrange
        Driver driver = new(new BlobPurgeJobRunRequest(250, PurgedCount: 7));
        driver.CompleteEmptyCycles(4);
        P.OrchestratorAction fetch = driver.SingleActivity(nameof(GetLargePayloadTombstonesActivity));
        driver.Turn(Driver.Configure(400), Driver.Configure(500));

        // Act - configuration is already delivered when the failed activity reaches the unsupported catch.
        driver.Turn(Driver.ActivityFailed(fetch, new NotImplementedException("unsupported")));
        P.CompleteOrchestrationAction can = Assert.Single(driver.Response.Actions).CompleteOrchestration;

        // Assert
        Assert.Equal(P.OrchestrationStatus.ContinuedAsNew, can.OrchestrationStatus);
        Assert.Equal("{\"PurgeBatchSize\":500,\"PurgedCount\":7}", can.Result);
        Assert.Empty(can.CarryoverEvents);
        Assert.Equal(driver.Response.ToByteArray(), driver.ReplayLastTurn().ToByteArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void UnsupportedWithInvalidBufferedConfiguration_FailsBeforeCan(int batch)
    {
        // Arrange
        Driver driver = new(new BlobPurgeJobRunRequest(250));
        driver.CompleteEmptyCycles(4);
        P.OrchestratorAction fetch = driver.SingleActivity(nameof(GetLargePayloadTombstonesActivity));
        driver.Turn(Driver.Configure(400), Driver.Configure(batch));

        // Act
        driver.Turn(Driver.ActivityFailed(fetch, new NotImplementedException("unsupported")));
        P.CompleteOrchestrationAction can = Assert.Single(driver.Response.Actions).CompleteOrchestration;

        // Assert
        Assert.Equal(P.OrchestrationStatus.Failed, can.OrchestrationStatus);
        Assert.Contains("ArgumentException", can.FailureDetails.ErrorType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void InvalidInputOrConfiguration_FailsExplicitly(int batch)
    {
        // Arrange
        Driver invalidInput = new(new BlobPurgeJobRunRequest(batch));
        Driver invalidEvent = new(new BlobPurgeJobRunRequest(250), Driver.Configure(batch));
        invalidEvent.Complete(invalidEvent.SingleActivity(nameof(GetLargePayloadTombstonesActivity)), Array.Empty<LargePayloadTombstone>());

        // Act / Assert
        foreach (Driver driver in new[] { invalidInput, invalidEvent })
        {
            P.CompleteOrchestrationAction result = Assert.Single(driver.Response.Actions).CompleteOrchestration;
            Assert.NotNull(result);
            Assert.Equal(P.OrchestrationStatus.Failed, result.OrchestrationStatus);
            Assert.Contains("ArgumentException", result.FailureDetails.ErrorType);
        }
    }

    sealed class Driver
    {
        readonly List<P.HistoryEvent> history = [];
        P.OrchestratorRequest lastRequest = null!;
        int eventId = 10000;
        public Driver(BlobPurgeJobRunRequest input, params P.HistoryEvent[] events)
        {
            P.HistoryEvent start = new()
            {
                EventId = -1,
                ExecutionStarted = new P.ExecutionStartedEvent
                {
                    Name = nameof(BlobPurgeJobOrchestrator), Input = JsonDataConverter.Default.Serialize(input),
                    OrchestrationInstance = new P.OrchestrationInstance { InstanceId = BlobPurgeConstants.OrchestratorInstanceId, ExecutionId = "executor-run" },
                },
            };
            this.Turn([start, .. events]);
        }
        public DateTime Now { get; private set; } = new(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc);
        public P.OrchestratorResponse Response { get; private set; } = null!;
        public void CompleteEmptyCycles(int count)
        {
            for (int i = 0; i < count; i++)
            {
                this.Complete(this.SingleActivity(nameof(GetLargePayloadTombstonesActivity)), Array.Empty<LargePayloadTombstone>());
                this.Turn(TimerFired(Assert.Single(this.Response.Actions)));
            }
        }

        public P.OrchestratorAction SingleActivity(string name)
        {
            P.OrchestratorAction result = Assert.Single(this.Response.Actions);
            Assert.NotNull(result.ScheduleTask);
            Assert.Equal(name, result.ScheduleTask.Name);
            return result;
        }
        public void Complete(P.OrchestratorAction action, object? value) => this.Turn(ActivityCompleted(action, value));
        public static P.HistoryEvent Configure(int batch) => new() { EventRaised = new P.EventRaisedEvent { Name = BlobPurgeConstants.SetBatchSizeEvent, Input = JsonDataConverter.Default.Serialize(batch) } };
        public static P.HistoryEvent ActivityCompleted(P.OrchestratorAction action, object? value) => new() { TaskCompleted = new P.TaskCompletedEvent { TaskScheduledId = action.Id, Result = JsonDataConverter.Default.Serialize(value) } };
        public static P.HistoryEvent ActivityFailed(P.OrchestratorAction action, Exception error) => new()
        {
            TaskFailed = new P.TaskFailedEvent
            {
                TaskScheduledId = action.Id,
                FailureDetails = new P.TaskFailureDetails { ErrorType = error.GetType().FullName, ErrorMessage = error.Message, IsNonRetriable = true },
            },
        };
        public static P.HistoryEvent TimerFired(P.OrchestratorAction action) => new() { TimerFired = new P.TimerFiredEvent { TimerId = action.Id, FireAt = action.CreateTimer.FireAt } };

        public void Turn(params P.HistoryEvent[] events)
        {
            this.Now = events.Where(e => e.TimerFired is not null).Select(e => e.TimerFired.FireAt.ToDateTime())
                .Append(this.Now.AddSeconds(1)).Max();
            this.lastRequest = new P.OrchestratorRequest
            {
                InstanceId = BlobPurgeConstants.OrchestratorInstanceId,
                PastEvents = { this.history.Select(e => e.Clone()) },
            };
            this.lastRequest.NewEvents.Add(new P.HistoryEvent
            {
                EventId = -1, Timestamp = Timestamp.FromDateTime(this.Now), OrchestratorStarted = new P.OrchestratorStartedEvent(),
            });
            foreach (P.HistoryEvent item in events)
            {
                item.Timestamp = Timestamp.FromDateTime(this.Now);
                item.EventId = this.eventId++;
                this.lastRequest.NewEvents.Add(item);
            }
            this.Response = this.ReplayLastTurn();
            this.history.AddRange(this.lastRequest.NewEvents.Select(e => e.Clone()));
            foreach (P.OrchestratorAction action in this.Response.Actions)
            {
                if (action.ScheduleTask is { } task)
                {
                    this.history.Add(new P.HistoryEvent
                    {
                        EventId = action.Id, Timestamp = Timestamp.FromDateTime(this.Now),
                        TaskScheduled = new P.TaskScheduledEvent { Name = task.Name, Input = task.Input, Version = task.Version },
                    });
                }
                if (action.CreateTimer is { } timer)
                {
                    this.history.Add(new P.HistoryEvent { EventId = action.Id, Timestamp = Timestamp.FromDateTime(this.Now), TimerCreated = new P.TimerCreatedEvent { FireAt = timer.FireAt } });
                }
            }
            this.history.Add(new P.HistoryEvent { EventId = -1, Timestamp = Timestamp.FromDateTime(this.Now), OrchestratorCompleted = new P.OrchestratorCompletedEvent() });
        }
        public P.OrchestratorResponse ReplayLastTurn() => P.OrchestratorResponse.Parser.ParseFrom(
            Convert.FromBase64String(GrpcOrchestrationRunner.LoadAndRun(
                Convert.ToBase64String(this.lastRequest.ToByteArray()), new BlobPurgeJobOrchestrator())));
    }
}
