// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using FluentAssertions;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Grpc.Tests;

/// <summary>
/// Focused unit tests for <see cref="AzureBlobPayloadsSideCarInterceptor"/>'s bounded-concurrency
/// payload externalization/resolution logic (see GitHub issue #775: "Performance: avoid serial
/// Azure Blob payload operations for fan-out messages"). These tests invoke the interceptor's
/// protected ExternalizeRequestPayloadsAsync/ResolveResponsePayloadsAsync methods directly via
/// reflection against an in-memory <see cref="PayloadStore"/> test double, without requiring a
/// running gRPC sidecar.
/// </summary>
public sealed class AzureBlobPayloadsSideCarInterceptorTests
{
    static readonly MethodInfo ExternalizeMethodDefinition = typeof(AzureBlobPayloadsSideCarInterceptor)
        .GetMethod("ExternalizeRequestPayloadsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

    static readonly MethodInfo ResolveMethodDefinition = typeof(AzureBlobPayloadsSideCarInterceptor)
        .GetMethod("ResolveResponsePayloadsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

    static readonly MethodInfo RunWithBoundedConcurrencyMethodDefinition = typeof(AzureBlobPayloadsSideCarInterceptor)
        .GetMethod("RunWithBoundedConcurrencyAsync", BindingFlags.Static | BindingFlags.NonPublic)!;

    [Fact]
    public async Task ResolveResponsePayloadsAsync_HistoryChunk_ResolvesEventsInOrderWithBoundedConcurrency()
    {
        // Arrange: 20 independent events, enough to observe 8-way bounded concurrency overlap.
        const int eventCount = 20;
        TrackingPayloadStore store = new(delay: TimeSpan.FromMilliseconds(50));
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, CreateOptions());

        P.HistoryChunk chunk = new();
        string[] expected = new string[eventCount];
        for (int i = 0; i < eventCount; i++)
        {
            expected[i] = $"payload-{i}";
            chunk.Events.Add(new P.HistoryEvent
            {
                EventId = i,
                ExecutionStarted = new P.ExecutionStartedEvent { Input = store.Seed(expected[i]) },
            });
        }

        // Act
        await ResolveAsync(interceptor, chunk, CancellationToken.None);

        // Assert: every event resolved to the correct value, in the correct position.
        for (int i = 0; i < eventCount; i++)
        {
            chunk.Events[i].ExecutionStarted.Input.Should().Be(expected[i]);
        }

        store.DownloadCount.Should().Be(eventCount);
        store.MaxObservedConcurrency.Should().BeGreaterThan(1, "independent payload operations should overlap, not run strictly sequentially");
        store.MaxObservedConcurrency.Should().BeLessOrEqualTo(8, "concurrency must be bounded to avoid Azure Storage throttling");
    }

    [Fact]
    public async Task ResolveResponsePayloadsAsync_QueryInstancesResponse_ResolvesEachInstanceIndependently()
    {
        // Arrange
        const int instanceCount = 12;
        TrackingPayloadStore store = new();
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, CreateOptions());

        P.QueryInstancesResponse response = new();
        string[] expectedInputs = new string[instanceCount];
        string[] expectedOutputs = new string[instanceCount];
        for (int i = 0; i < instanceCount; i++)
        {
            expectedInputs[i] = $"input-{i}";
            expectedOutputs[i] = $"output-{i}";
            response.OrchestrationState.Add(new P.OrchestrationState
            {
                InstanceId = $"instance-{i}",
                Input = store.Seed(expectedInputs[i]),
                Output = store.Seed(expectedOutputs[i]),
            });
        }

        // Act
        await ResolveAsync(interceptor, response, CancellationToken.None);

        // Assert: no cross-instance mixups under concurrent resolution.
        for (int i = 0; i < instanceCount; i++)
        {
            response.OrchestrationState[i].Input.Should().Be(expectedInputs[i]);
            response.OrchestrationState[i].Output.Should().Be(expectedOutputs[i]);
        }
    }

    [Fact]
    public async Task ResolveResponsePayloadsAsync_WorkItemOrchestratorRequest_ResolvesPastAndNewEventsInOrder()
    {
        // Arrange
        TrackingPayloadStore store = new();
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, CreateOptions());

        P.WorkItem workItem = new()
        {
            OrchestratorRequest = new P.OrchestratorRequest { InstanceId = "instance-1" },
        };

        string[] pastExpected = ["past-0", "past-1", "past-2"];
        string[] newExpected = ["new-0", "new-1"];

        foreach (string value in pastExpected)
        {
            workItem.OrchestratorRequest.PastEvents.Add(new P.HistoryEvent
            {
                TaskScheduled = new P.TaskScheduledEvent { Input = store.Seed(value) },
            });
        }

        foreach (string value in newExpected)
        {
            workItem.OrchestratorRequest.NewEvents.Add(new P.HistoryEvent
            {
                TaskCompleted = new P.TaskCompletedEvent { Result = store.Seed(value) },
            });
        }

        // Act
        await ResolveAsync(interceptor, workItem, CancellationToken.None);

        // Assert: past and new events resolve independently without swapping values across lists/positions.
        for (int i = 0; i < pastExpected.Length; i++)
        {
            workItem.OrchestratorRequest.PastEvents[i].TaskScheduled.Input.Should().Be(pastExpected[i]);
        }

        for (int i = 0; i < newExpected.Length; i++)
        {
            workItem.OrchestratorRequest.NewEvents[i].TaskCompleted.Result.Should().Be(newExpected[i]);
        }
    }

    [Fact]
    public async Task ResolveResponsePayloadsAsync_WorkItemEntityRequestV1_ResolvesEntityStateAndOperationsInOrder()
    {
        // Arrange
        TrackingPayloadStore store = new();
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, CreateOptions());

        P.WorkItem workItem = new()
        {
            EntityRequest = new P.EntityBatchRequest { InstanceId = "entity-1", EntityState = store.Seed("entity-state") },
        };

        const int operationCount = 6;
        string[] expected = new string[operationCount];
        for (int i = 0; i < operationCount; i++)
        {
            expected[i] = $"op-{i}";
            workItem.EntityRequest.Operations.Add(new P.OperationRequest
            {
                Operation = "op",
                RequestId = $"req-{i}",
                Input = store.Seed(expected[i]),
            });
        }

        // Act
        await ResolveAsync(interceptor, workItem, CancellationToken.None);

        // Assert
        workItem.EntityRequest.EntityState.Should().Be("entity-state");
        for (int i = 0; i < operationCount; i++)
        {
            workItem.EntityRequest.Operations[i].Input.Should().Be(expected[i]);
        }
    }

    [Fact]
    public async Task ExternalizeRequestPayloadsAsync_OrchestratorResponse_ExternalizesActionsInOrderWithBoundedConcurrency()
    {
        // Arrange: 16 independent actions, enough to observe 8-way bounded concurrency overlap.
        const int actionCount = 16;
        TrackingPayloadStore store = new(delay: TimeSpan.FromMilliseconds(50));
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, CreateOptions());

        P.OrchestratorResponse response = new() { InstanceId = "instance-1", CustomStatus = "status-0" };
        string[] expectedInputs = new string[actionCount];
        for (int i = 0; i < actionCount; i++)
        {
            expectedInputs[i] = $"action-input-{i}";
            response.Actions.Add(new P.OrchestratorAction
            {
                Id = i,
                ScheduleTask = new P.ScheduleTaskAction { Name = $"activity-{i}", Input = expectedInputs[i] },
            });
        }

        // Act
        await ExternalizeAsync(interceptor, response, CancellationToken.None);

        // Assert: every action's input externalized to a distinct token round-tripping to the correct value.
        store.GetUploadedValue(response.CustomStatus).Should().Be("status-0");
        for (int i = 0; i < actionCount; i++)
        {
            string token = response.Actions[i].ScheduleTask.Input;
            token.Should().NotBe(expectedInputs[i]);
            store.GetUploadedValue(token).Should().Be(expectedInputs[i]);
        }

        store.MaxObservedConcurrency.Should().BeGreaterThan(1, "independent payload operations should overlap, not run strictly sequentially");
        store.MaxObservedConcurrency.Should().BeLessOrEqualTo(8, "concurrency must be bounded to avoid Azure Storage throttling");
    }

    [Fact]
    public async Task ExternalizeRequestPayloadsAsync_EntityBatchRequest_ExternalizesOperationsInOrder()
    {
        // Arrange
        TrackingPayloadStore store = new();
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, CreateOptions());

        const int operationCount = 10;
        P.EntityBatchRequest request = new() { InstanceId = "entity-1", EntityState = "state-0" };
        string[] expectedInputs = new string[operationCount];
        for (int i = 0; i < operationCount; i++)
        {
            expectedInputs[i] = $"op-input-{i}";
            request.Operations.Add(new P.OperationRequest { Operation = "op", RequestId = $"req-{i}", Input = expectedInputs[i] });
        }

        // Act
        await ExternalizeAsync(interceptor, request, CancellationToken.None);

        // Assert
        store.GetUploadedValue(request.EntityState).Should().Be("state-0");
        for (int i = 0; i < operationCount; i++)
        {
            string token = request.Operations[i].Input;
            token.Should().NotBe(expectedInputs[i]);
            store.GetUploadedValue(token).Should().Be(expectedInputs[i]);
        }
    }

    [Fact]
    public async Task ExternalizeRequestPayloadsAsync_EntityBatchResult_ExternalizesResultsAndActionsInOrder()
    {
        // Arrange
        TrackingPayloadStore store = new();
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, CreateOptions());

        P.EntityBatchResult result = new() { EntityState = "state-final" };

        const int resultCount = 6;
        string[] expectedResults = new string[resultCount];
        for (int i = 0; i < resultCount; i++)
        {
            expectedResults[i] = $"success-result-{i}";
            result.Results.Add(new P.OperationResult { Success = new P.OperationResultSuccess { Result = expectedResults[i] } });
        }

        result.Actions.Add(new P.OperationAction
        {
            Id = 1,
            SendSignal = new P.SendSignalAction { InstanceId = "target-1", Name = "signal", Input = "signal-input" },
        });
        result.Actions.Add(new P.OperationAction
        {
            Id = 2,
            StartNewOrchestration = new P.StartNewOrchestrationAction { InstanceId = "target-2", Name = "orch", Input = "start-input" },
        });

        // Act
        await ExternalizeAsync(interceptor, result, CancellationToken.None);

        // Assert
        store.GetUploadedValue(result.EntityState).Should().Be("state-final");
        for (int i = 0; i < resultCount; i++)
        {
            store.GetUploadedValue(result.Results[i].Success.Result).Should().Be(expectedResults[i]);
        }

        store.GetUploadedValue(result.Actions[0].SendSignal.Input).Should().Be("signal-input");
        store.GetUploadedValue(result.Actions[1].StartNewOrchestration.Input).Should().Be("start-input");
    }

    [Fact]
    public async Task ExternalizeRequestPayloadsAsync_ActivityResponse_PermanentFailure_ConvertsToFailureDetails()
    {
        // Arrange: MaxPayloadBytes = 10 so the oversized result triggers a permanent PayloadStorageException.
        TrackingPayloadStore store = new();
        LargePayloadStorageOptions options = new() { ThresholdBytes = 1, MaxPayloadBytes = 10 };
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, options);

        P.ActivityResponse response = new() { InstanceId = "instance-1", TaskId = 1, Result = new string('y', 100) };

        // Act
        await ExternalizeAsync(interceptor, response, CancellationToken.None);

        // Assert: activity result replaced with non-retriable failure details instead of throwing/abandoning the work item.
        response.Result.Should().BeNullOrEmpty();
        response.FailureDetails.Should().NotBeNull();
        response.FailureDetails.ErrorType.Should().Be(typeof(PayloadStorageException).FullName);
        response.FailureDetails.IsNonRetriable.Should().BeTrue();
        store.UploadCount.Should().Be(0, "the oversized payload should fail before ever reaching the payload store");
    }

    [Fact]
    public async Task ExternalizeRequestPayloadsAsync_OrchestratorResponse_PermanentFailureInOneAction_ReplacesActionsWithFailedCompletion()
    {
        // Arrange: MaxPayloadBytes = 10 so the second action's oversized input triggers a permanent failure.
        TrackingPayloadStore store = new();
        LargePayloadStorageOptions options = new() { ThresholdBytes = 1, MaxPayloadBytes = 10 };
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, options);

        P.OrchestratorResponse response = new() { InstanceId = "instance-1", CustomStatus = "status" };
        response.Actions.Add(new P.OrchestratorAction
        {
            Id = 1,
            ScheduleTask = new P.ScheduleTaskAction { Name = "activity-1", Input = "short" },
        });
        response.Actions.Add(new P.OrchestratorAction
        {
            Id = 2,
            ScheduleTask = new P.ScheduleTaskAction { Name = "activity-2", Input = new string('x', 100) },
        });

        // Act
        await ExternalizeAsync(interceptor, response, CancellationToken.None);

        // Assert: entire response replaced with a single Failed completion action, per the pre-existing
        // (unrefactored) fallback contract for permanent externalization failures.
        response.Actions.Should().HaveCount(1);
        P.OrchestratorAction resultAction = response.Actions[0];
        resultAction.CompleteOrchestration.Should().NotBeNull();
        resultAction.CompleteOrchestration.OrchestrationStatus.Should().Be(P.OrchestrationStatus.Failed);
        resultAction.CompleteOrchestration.FailureDetails.ErrorType.Should().Be(typeof(PayloadStorageException).FullName);
        resultAction.CompleteOrchestration.FailureDetails.IsNonRetriable.Should().BeTrue();
        response.CustomStatus.Should().BeNullOrEmpty();
#pragma warning disable CS0612 // isPartial/chunkIndex are deprecated but still required for chunked response wire compatibility.
        response.IsPartial.Should().BeFalse();
        response.ChunkIndex.Should().BeNull();
#pragma warning restore CS0612
    }

    [Fact]
    public async Task ResolveResponsePayloadsAsync_PreCancelledToken_ThrowsAndPerformsNoStoreCalls()
    {
        // Arrange
        TrackingPayloadStore store = new();
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, CreateOptions());

        P.HistoryChunk chunk = new();
        for (int i = 0; i < 5; i++)
        {
            chunk.Events.Add(new P.HistoryEvent
            {
                EventId = i,
                ExecutionStarted = new P.ExecutionStartedEvent { Input = store.Seed($"value-{i}") },
            });
        }

        using CancellationTokenSource cts = new();
        cts.Cancel();

        // Act
        Func<Task> act = () => ResolveAsync(interceptor, chunk, cts.Token);

        // Assert: cancellation observed before any operation starts (no store calls made).
        await act.Should().ThrowAsync<OperationCanceledException>();
        store.DownloadCount.Should().Be(0);
    }

    [Fact]
    public async Task RunWithBoundedConcurrencyAsync_CancelledWhileOperationsInFlight_DrainsBeforeReturningAndDoesNotMaskFailure()
    {
        // Arrange: 9 operations -- one more than MaxConcurrentPayloadOperations (8) -- so the
        // dispatch loop fills all 8 concurrency slots with genuinely in-flight operations before
        // the 9th operation's semaphore wait forces the loop to observe real contention. This is
        // the exact race window in which the original bug -- disposing the bounding SemaphoreSlim
        // on cancellation before every in-flight operation had released it -- could surface an
        // unobserved ObjectDisposedException, mask a genuine (non-cancellation) failure from an
        // already-in-flight operation, and let that operation's continuation run after the caller
        // had already received a response.
        //
        // Operation 0 simulates an upload/download that has already committed and is in the
        // process of failing for a real, unrelated reason (e.g. a permanent PayloadStorageException)
        // -- it does not observe the cancellation token at all, so it only completes once the test
        // explicitly releases it, well after cancellation has been requested. Operations 1-6
        // likewise ignore cancellation and simply succeed once released. Operation 7 (the 9th, index
        // 8) must never be dispatched at all, since cancellation is requested before a 9th slot ever
        // frees up.
        TaskCompletionSource<bool> releaseGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int dispatchedBeforeCancel = 0;
        bool ninthOperationDispatched = false;

        List<Func<Task>> operations = [];
        operations.Add(async () =>
        {
            Interlocked.Increment(ref dispatchedBeforeCancel);
            await releaseGate.Task;
            throw new InvalidOperationException("Simulated permanent storage failure, unrelated to cancellation.");
        });
        for (int i = 1; i < 8; i++)
        {
            operations.Add(async () =>
            {
                Interlocked.Increment(ref dispatchedBeforeCancel);
                await releaseGate.Task;
            });
        }

        operations.Add(() =>
        {
            // Should never run: cancellation must stop the dispatch loop before this 9th
            // operation's semaphore wait can ever be satisfied.
            ninthOperationDispatched = true;
            return Task.CompletedTask;
        });

        using CancellationTokenSource cts = new();

        // Act: start the call without awaiting it yet. The first 8 operations dispatch and block
        // on `releaseGate` synchronously (no artificial delay is needed since none of them observe
        // cancellation), so by the time this line completes, the 9th operation's semaphore wait is
        // already the sole blocking point.
        Task runTask = RunWithBoundedConcurrencyAsync(operations, cts.Token);
        dispatchedBeforeCancel.Should().Be(8, "the first 8 operations should dispatch synchronously before the 9th blocks on the semaphore");

        cts.Cancel();

        // Small buffer so that, under the old (buggy) implementation, the cancellation-triggered
        // unwind and premature semaphore disposal have already fully happened before the
        // already-in-flight operations are released below -- making the demonstration
        // unambiguous rather than a race between this test and that unwind.
        await Task.Delay(50);

        // Now let the 8 already-in-flight operations complete: operation 0 fails for a genuine,
        // cancellation-unrelated reason; operations 1-6 succeed.
        releaseGate.SetResult(true);

        // Assert: the overall call surfaces the genuine InvalidOperationException -- not
        // OperationCanceledException (which the old implementation would incorrectly surface
        // immediately upon cancellation, before operation 0 ever got a chance to fail) and not
        // ObjectDisposedException (which the old implementation could surface from an orphaned,
        // unobserved background task once operation 0's `finally` tried to release an
        // already-disposed semaphore). A bounded wait guards against a deadlock regression.
        Func<Task> act = () => runTask.WaitAsync(TimeSpan.FromSeconds(10));
        (await act.Should().ThrowExactlyAsync<InvalidOperationException>(
            "a genuine failure from an already-in-flight operation must never be masked by a concurrent cancellation or disposal race"))
            .WithMessage("Simulated permanent storage failure*");

        // Assert: the 9th operation was never dispatched -- cancellation correctly stopped the
        // dispatch loop from starting new operations, without abandoning the 8 already in flight.
        ninthOperationDispatched.Should().BeFalse();
    }

    static LargePayloadStorageOptions CreateOptions() => new() { ThresholdBytes = 1 };

    static Task ExternalizeAsync<TRequest>(AzureBlobPayloadsSideCarInterceptor interceptor, TRequest request, CancellationToken cancellation)
        => (Task)ExternalizeMethodDefinition.MakeGenericMethod(typeof(TRequest)).Invoke(interceptor, [request, cancellation])!;

    static Task ResolveAsync<TResponse>(AzureBlobPayloadsSideCarInterceptor interceptor, TResponse response, CancellationToken cancellation)
        => (Task)ResolveMethodDefinition.MakeGenericMethod(typeof(TResponse)).Invoke(interceptor, [response, cancellation])!;

    static Task RunWithBoundedConcurrencyAsync(IReadOnlyList<Func<Task>> operations, CancellationToken cancellation)
        => (Task)RunWithBoundedConcurrencyMethodDefinition.Invoke(null, [operations, cancellation])!;

    /// <summary>
    /// In-memory <see cref="PayloadStore"/> test double that tracks upload/download counts and the
    /// high-water mark of concurrently in-flight calls, and optionally introduces an artificial
    /// delay to force overlapping calls so bounded concurrency can be observed deterministically.
    /// </summary>
    sealed class TrackingPayloadStore(TimeSpan delay = default) : PayloadStore
    {
        const string TokenPrefix = "test-blob://";

        readonly object gate = new();
        readonly Dictionary<string, string> tokenToValue = [];
        int concurrentCalls;
        int uploadCount;
        int downloadCount;

        public int MaxObservedConcurrency { get; private set; }

        public int UploadCount => this.uploadCount;

        public int DownloadCount => this.downloadCount;

        /// <summary>Pre-populates the store with a value and returns its token, for resolve-direction tests.</summary>
        public string Seed(string value)
        {
            string token = TokenPrefix + Guid.NewGuid().ToString("N");
            lock (this.gate)
            {
                this.tokenToValue[token] = value;
            }

            return token;
        }

        /// <summary>Looks up the original value uploaded/seeded for a given token, for externalize-direction tests.</summary>
        public string GetUploadedValue(string token)
        {
            lock (this.gate)
            {
                return this.tokenToValue[token];
            }
        }

        public override async Task<string> UploadAsync(string payLoad, CancellationToken cancellationToken)
        {
            this.EnterCall();
            try
            {
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }

                Interlocked.Increment(ref this.uploadCount);
                string token = TokenPrefix + Guid.NewGuid().ToString("N");
                lock (this.gate)
                {
                    this.tokenToValue[token] = payLoad;
                }

                return token;
            }
            finally
            {
                this.ExitCall();
            }
        }

        public override async Task<string> DownloadAsync(string token, CancellationToken cancellationToken)
        {
            this.EnterCall();
            try
            {
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }

                Interlocked.Increment(ref this.downloadCount);
                lock (this.gate)
                {
                    return this.tokenToValue[token];
                }
            }
            finally
            {
                this.ExitCall();
            }
        }

        public override bool IsKnownPayloadToken(string value) => value.StartsWith(TokenPrefix, StringComparison.Ordinal);

        void EnterCall()
        {
            lock (this.gate)
            {
                this.concurrentCalls++;
                if (this.concurrentCalls > this.MaxObservedConcurrency)
                {
                    this.MaxObservedConcurrency = this.concurrentCalls;
                }
            }
        }

        void ExitCall()
        {
            lock (this.gate)
            {
                this.concurrentCalls--;
            }
        }
    }
}
