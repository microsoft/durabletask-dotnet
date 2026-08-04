// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Azure;
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

    static readonly PropertyInfo BeforeSharedMessageLockForTestProperty = typeof(AzureBlobPayloadsSideCarInterceptor)
        .GetProperty("BeforeSharedMessageLockForTest", BindingFlags.Instance | BindingFlags.NonPublic)!;

    static readonly PropertyInfo SharedMessageLockAcquiredForTestProperty = typeof(AzureBlobPayloadsSideCarInterceptor)
        .GetProperty("SharedMessageLockAcquiredForTest", BindingFlags.Instance | BindingFlags.NonPublic)!;

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
    public async Task ResolveResponsePayloadsAsync_QueryInstancesResponse_UsesFieldSnapshotsForQueuedOperations()
    {
        // Arrange: 3 states produce 9 operations, so the final CustomStatus download waits behind
        // the concurrency limit of 8. Mutating that field after dispatch starts must not change
        // which token the already-built operation resolves.
        TaskCompletionSource<bool> firstWaveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> releaseFirstWave = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<string> queuedTokenObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int downloadsStarted = 0;
        TrackingPayloadStore store = new(downloadAsync: async (token, cancellationToken) =>
        {
            int ordinal = Interlocked.Increment(ref downloadsStarted);
            if (ordinal <= 8)
            {
                if (ordinal == 8)
                {
                    firstWaveStarted.TrySetResult(true);
                }

                await releaseFirstWave.Task.WaitAsync(cancellationToken);
            }
            else
            {
                queuedTokenObserved.TrySetResult(token);
            }

            return token["test-blob://".Length..];
        });
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, CreateOptions());
        P.QueryInstancesResponse response = new();
        for (int i = 0; i < 3; i++)
        {
            response.OrchestrationState.Add(new P.OrchestrationState
            {
                InstanceId = $"instance-{i}",
                Input = $"test-blob://state-{i}-input",
                Output = $"test-blob://state-{i}-output",
                CustomStatus = i == 2 ? "test-blob://original-status" : $"test-blob://state-{i}-status",
            });
        }

        // Act: once the first 8 operations are in flight, change the field backing the queued
        // ninth delegate. The delegate must use the value captured while the list was built.
        Task resolveTask = ResolveAsync(interceptor, response, CancellationToken.None);
        await firstWaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        P.OrchestrationState queuedState = response.OrchestrationState[2];
        lock (queuedState)
        {
            queuedState.CustomStatus = "test-blob://changed-status";
        }

        releaseFirstWave.SetResult(true);
        await resolveTask.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        (await queuedTokenObserved.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be("test-blob://original-status");
        queuedState.CustomStatus.Should().Be("original-status");
    }

    [Fact]
    public async Task ResolveResponsePayloadsAsync_GetInstanceResponse_SerializesSharedStateMutationsAfterConcurrentDownloads()
    {
        // Arrange: all three fields belong to the same OrchestrationState message. The test holds
        // that message's monitor while allowing the downloads to finish. A test-only callback
        // synchronously signals immediately before each assignment attempts that monitor.
        TaskCompletionSource<bool> allDownloadsStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> releaseDownloads = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int downloadsStarted = 0;
        TrackingPayloadStore store = new(downloadAsync: async (token, cancellationToken) =>
        {
            if (Interlocked.Increment(ref downloadsStarted) == 3)
            {
                allDownloadsStarted.TrySetResult(true);
            }

            await releaseDownloads.Task;
            return token switch
            {
                "test-blob://input" => "input",
                "test-blob://output" => "output",
                "test-blob://status" => "status",
                _ => throw new InvalidOperationException($"Unexpected token: {token}"),
            };
        });
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, CreateOptions());
        P.OrchestrationState state = new()
        {
            Input = "test-blob://input",
            Output = "test-blob://output",
            CustomStatus = "test-blob://status",
        };
        P.GetInstanceResponse response = new() { OrchestrationState = state };
        TaskCompletionSource<bool> firstAssignmentReachedLock = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int assignmentsReachedLock = 0;
        int messageLockAcquisitions = 0;
        BeforeSharedMessageLockForTestProperty.SetValue(interceptor, (Action)(() =>
        {
            if (Interlocked.Increment(ref assignmentsReachedLock) == 1)
            {
                firstAssignmentReachedLock.TrySetResult(true);
            }
        }));
        SharedMessageLockAcquiredForTestProperty.SetValue(interceptor, (Action<object>)(message =>
        {
            message.Should().BeSameAs(state);
            Monitor.IsEntered(message).Should().BeTrue();
            Interlocked.Increment(ref messageLockAcquisitions);
        }));

        TaskCompletionSource<bool> stateLockAcquired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> releaseStateLock = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task holdStateLock = Task.Factory.StartNew(
            () =>
            {
                lock (state)
                {
                    stateLockAcquired.SetResult(true);
                    releaseStateLock.Task.GetAwaiter().GetResult();
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        try
        {
            await stateLockAcquired.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // Act
            Task resolveTask = ResolveAsync(interceptor, response, CancellationToken.None);
            await allDownloadsStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            releaseDownloads.SetResult(true);
            await firstAssignmentReachedLock.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // Assert: at least one assignment has demonstrably reached the held monitor, yet none
            // can mutate the shared message. Blob reads still overlap, and the inside-lock hook
            // below proves all three assignments eventually acquire this exact monitor.
            resolveTask.IsCompleted.Should().BeFalse("an assignment must wait for the held protobuf-message monitor");
            state.Input.Should().Be("test-blob://input");
            state.Output.Should().Be("test-blob://output");
            state.CustomStatus.Should().Be("test-blob://status");
            Volatile.Read(ref assignmentsReachedLock).Should().BeGreaterThanOrEqualTo(1);
            Volatile.Read(ref messageLockAcquisitions).Should().Be(0);
            store.MaxObservedConcurrency.Should().BeGreaterThan(1, "independent Blob reads should still overlap");

            releaseStateLock.SetResult(true);
            await resolveTask.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            releaseDownloads.TrySetResult(true);
            releaseStateLock.TrySetResult(true);
            await holdStateLock;
        }

        state.Input.Should().Be("input");
        state.Output.Should().Be("output");
        state.CustomStatus.Should().Be("status");
        Volatile.Read(ref assignmentsReachedLock).Should().Be(3);
        Volatile.Read(ref messageLockAcquisitions).Should().Be(3);
    }

    [Fact]
    public async Task ResolveResponsePayloadsAsync_GetInstanceResponse_PreCancelledInlineFieldsPerformNoWork()
    {
        // Arrange: no field contains a payload token, so the response has no storage operation to
        // cancel. This preserves the pre-concurrency behavior where inline values return unchanged.
        TrackingPayloadStore store = new();
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, CreateOptions());
        P.GetInstanceResponse response = new()
        {
            OrchestrationState = new P.OrchestrationState
            {
                Input = "inline-input",
                Output = "inline-output",
                CustomStatus = "inline-status",
            },
        };
        using CancellationTokenSource cts = new();
        cts.Cancel();

        // Act
        Func<Task> act = () => ResolveAsync(interceptor, response, cts.Token);

        // Assert
        await act.Should().NotThrowAsync();
        store.DownloadCount.Should().Be(0);
        response.OrchestrationState.Input.Should().Be("inline-input");
        response.OrchestrationState.Output.Should().Be("inline-output");
        response.OrchestrationState.CustomStatus.Should().Be("inline-status");
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
    public async Task ExternalizeRequestPayloadsAsync_OrchestratorResponse_EarlierRetriableFailureWinsOverLaterPermanentFailure()
    {
        // Arrange: custom status is the first operation and blocks before failing with a retriable
        // Blob error. The action input is a later operation that fails immediately with a permanent
        // PayloadStorageException. Sequential behavior surfaces the earlier retriable failure, so
        // the response must not be converted to a terminal Failed completion.
        TaskCompletionSource<bool> firstUploadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> laterPermanentFailureObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> releaseFirstUpload = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TrackingPayloadStore store = new(uploadAsync: async (payload, cancellationToken) =>
        {
            if (payload == "retriable")
            {
                firstUploadStarted.SetResult(true);
                await releaseFirstUpload.Task.WaitAsync(cancellationToken);
                throw new RequestFailedException(500, "Simulated retriable Blob failure.", "ServerError", null);
            }

            if (payload == "permanent")
            {
                laterPermanentFailureObserved.SetResult(true);
                throw new PayloadStorageException("Simulated permanent payload failure.");
            }

            throw new InvalidOperationException($"Unexpected payload: {payload}");
        });
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, CreateOptions());
        P.OrchestratorResponse response = new() { InstanceId = "instance-1", CustomStatus = "retriable" };
        response.Actions.Add(new P.OrchestratorAction
        {
            Id = 1,
            ScheduleTask = new P.ScheduleTaskAction { Name = "activity", Input = "permanent" },
        });

        // Act: wait until both operations have failed or are pending, then release the earlier
        // operation so its lower-ordinal retriable failure completes after the permanent one.
        Task externalizeTask = ExternalizeAsync(interceptor, response, CancellationToken.None);
        await firstUploadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await laterPermanentFailureObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
        releaseFirstUpload.SetResult(true);

        // Assert: message order, rather than completion timing, chooses the failure. The retriable
        // Blob error propagates and the permanent later failure does not replace the response with
        // a terminal Failed completion.
        Func<Task> act = () => externalizeTask;
        (await act.Should().ThrowExactlyAsync<RequestFailedException>())
            .WithMessage("Simulated retriable Blob failure.");
        response.Actions.Should().ContainSingle();
        response.Actions[0].ScheduleTask.Should().NotBeNull();
        response.CustomStatus.Should().Be("retriable");
    }

    [Fact]
    public async Task ExternalizeRequestPayloadsAsync_OrchestratorResponse_SingleOperationPreCancelledTokenDoesNotStartUploadOrConvertResponse()
    {
        // Arrange: CustomStatus is the only externalizable field, so it uses the one-operation
        // fast path. The store's permanent failure is a sentinel: reaching it would cause the
        // caller's permanent-failure conversion unless cancellation is observed first.
        TrackingPayloadStore store = new(uploadAsync: (_, _) => throw new PayloadStorageException("Upload should not start."));
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, CreateOptions());
        P.OrchestratorResponse response = new() { InstanceId = "instance-1", CustomStatus = "status" };
        using CancellationTokenSource cts = new();
        cts.Cancel();

        // Act
        Func<Task> act = () => ExternalizeAsync(interceptor, response, cts.Token);

        // Assert: pre-cancellation prevents the upload and preserves the response rather than
        // converting the sentinel permanent failure into a terminal failed completion.
        await act.Should().ThrowAsync<OperationCanceledException>();
        store.UploadCount.Should().Be(0);
        response.CustomStatus.Should().Be("status");
        response.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task ExternalizeRequestPayloadsAsync_OrchestratorResponse_PreCancelledInlineFieldsPerformNoWork()
    {
        // Arrange: all values are below the externalization threshold, so cancellation has no Blob
        // operation to prevent and the response should remain untouched.
        TrackingPayloadStore store = new();
        LargePayloadStorageOptions options = new() { ThresholdBytes = 1024 };
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, options);
        P.OrchestratorResponse response = new()
        {
            InstanceId = "instance-1",
            CustomStatus = "inline-status",
        };
        response.Actions.Add(new P.OrchestratorAction
        {
            ScheduleTask = new P.ScheduleTaskAction { Input = "inline-input" },
        });
        using CancellationTokenSource cts = new();
        cts.Cancel();

        // Act
        Func<Task> act = () => ExternalizeAsync(interceptor, response, cts.Token);

        // Assert
        await act.Should().NotThrowAsync();
        store.UploadCount.Should().Be(0);
        response.CustomStatus.Should().Be("inline-status");
        response.Actions.Should().ContainSingle();
        response.Actions[0].ScheduleTask.Input.Should().Be("inline-input");
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
    public async Task RunWithBoundedConcurrencyAsync_InFlightRealFailureTakesPrecedenceOverCancellation()
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
        // explicitly releases it, well after cancellation has been requested. Operations 1-7
        // likewise ignore cancellation and simply succeed once released. Operation 8 (the 9th, index
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

    [Fact]
    public async Task RunWithBoundedConcurrencyAsync_DispatchCallbackThrows_DrainsClaimedOperationBeforeRethrow()
    {
        // Arrange: operation 0 is already claimed and blocked when the test-only callback throws
        // before operation 1 can claim its slot.
        TaskCompletionSource<bool> releaseClaimedOperation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int claimedOperationCompleted = 0;
        int pendingOperationStarted = 0;
        List<Func<Task>> operations =
        [
            async () =>
            {
                await releaseClaimedOperation.Task;
                Interlocked.Exchange(ref claimedOperationCompleted, 1);
            },
            () =>
            {
                Interlocked.Exchange(ref pendingOperationStarted, 1);
                return Task.CompletedTask;
            },
        ];
        Action<int> afterAdvisoryDispatchCheck = operationOrdinal =>
        {
            if (operationOrdinal == 1)
            {
                throw new InvalidOperationException("Test dispatch callback failed.");
            }
        };

        // Act
        Task runTask = RunWithBoundedConcurrencyAsync(
            operations,
            CancellationToken.None,
            afterAdvisoryDispatchCheck);

        // Assert: the callback failure stops the pending operation but does not abandon work that
        // was already claimed. The original callback exception is rethrown only after that work drains.
        try
        {
            runTask.IsCompleted.Should().BeFalse();
            Volatile.Read(ref pendingOperationStarted).Should().Be(0);
        }
        finally
        {
            releaseClaimedOperation.TrySetResult(true);
        }

        Func<Task> act = () => runTask.WaitAsync(TimeSpan.FromSeconds(10));
        (await act.Should().ThrowExactlyAsync<InvalidOperationException>())
            .WithMessage("Test dispatch callback failed.");
        Volatile.Read(ref claimedOperationCompleted).Should().Be(1);
    }

    [Fact]
    public async Task RunWithBoundedConcurrencyAsync_FailureRecordedBeforePendingDispatchClaim_DoesNotStartPendingOperation()
    {
        // Arrange: eight operations occupy the concurrency limit. Operation 0 succeeds to release
        // a slot. Operation 8 passes the advisory stop check, then a test seam pauses it immediately
        // before its authoritative dispatch claim.
        TaskCompletionSource<bool> releaseFirstSuccess = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> releaseFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> releaseRemaining = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> pendingDispatchReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> failureRecorded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using ManualResetEventSlim continueDispatchClaim = new(initialState: false);
        int operationsStarted = 0;
        int pendingOperationStarted = 0;

        List<Func<Task>> operations =
        [
            async () =>
            {
                Interlocked.Increment(ref operationsStarted);
                await releaseFirstSuccess.Task;
            },
            async () =>
            {
                Interlocked.Increment(ref operationsStarted);
                await releaseFailure.Task;
                throw new InvalidOperationException("Lower-ordinal operation failed.");
            },
        ];
        for (int i = 2; i < 8; i++)
        {
            operations.Add(async () =>
            {
                Interlocked.Increment(ref operationsStarted);
                await releaseRemaining.Task;
            });
        }

        operations.Add(() =>
        {
            Interlocked.Exchange(ref pendingOperationStarted, 1);
            return Task.CompletedTask;
        });

        Action<int> afterAdvisoryDispatchCheck = operationOrdinal =>
        {
            if (operationOrdinal == 8)
            {
                pendingDispatchReached.TrySetResult(true);
                continueDispatchClaim.Wait();
            }
        };
        Action<int> onFailureRecorded = operationOrdinal =>
        {
            if (operationOrdinal == 1)
            {
                failureRecorded.TrySetResult(true);
            }
        };

        Task runTask = RunWithBoundedConcurrencyAsync(
            operations,
            CancellationToken.None,
            afterAdvisoryDispatchCheck,
            onFailureRecorded);
        operationsStarted.Should().Be(8);

        try
        {
            // Act: let operation 8 pass advisory eligibility, then record operation 1's failure
            // before allowing the authoritative claim to continue.
            releaseFirstSuccess.SetResult(true);
            await pendingDispatchReached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            releaseFailure.SetResult(true);
            await failureRecorded.Task.WaitAsync(TimeSpan.FromSeconds(10));
            continueDispatchClaim.Set();
            releaseRemaining.SetResult(true);

            // Assert: the recorded failure wins the atomic claim and operation 8 never begins.
            Func<Task> act = () => runTask.WaitAsync(TimeSpan.FromSeconds(10));
            (await act.Should().ThrowExactlyAsync<InvalidOperationException>())
                .WithMessage("Lower-ordinal operation failed.");
            Volatile.Read(ref pendingOperationStarted).Should().Be(0);
        }
        finally
        {
            releaseFirstSuccess.TrySetResult(true);
            releaseFailure.TrySetResult(true);
            releaseRemaining.TrySetResult(true);
            continueDispatchClaim.Set();
        }
    }

    [Fact]
    public async Task RunWithBoundedConcurrencyAsync_CancellationAfterAllOperationsComplete_ReturnsSuccessfully()
    {
        // Arrange: the final operation requests cancellation only after it has already claimed
        // dispatch; both operations complete successfully.
        using CancellationTokenSource cts = new();
        int completedOperations = 0;
        List<Func<Task>> operations =
        [
            () =>
            {
                Interlocked.Increment(ref completedOperations);
                return Task.CompletedTask;
            },
            () =>
            {
                Interlocked.Increment(ref completedOperations);
                cts.Cancel();
                return Task.CompletedTask;
            },
        ];

        // Act
        Func<Task> act = () => RunWithBoundedConcurrencyAsync(operations, cts.Token);

        // Assert: cancellation did not prevent a dispatch, so successful work remains successful.
        await act.Should().NotThrowAsync();
        completedOperations.Should().Be(2);
    }

    static LargePayloadStorageOptions CreateOptions() => new() { ThresholdBytes = 1 };

    static Task ExternalizeAsync<TRequest>(AzureBlobPayloadsSideCarInterceptor interceptor, TRequest request, CancellationToken cancellation)
        => (Task)ExternalizeMethodDefinition.MakeGenericMethod(typeof(TRequest)).Invoke(interceptor, [request, cancellation])!;

    static Task ResolveAsync<TResponse>(AzureBlobPayloadsSideCarInterceptor interceptor, TResponse response, CancellationToken cancellation)
        => (Task)ResolveMethodDefinition.MakeGenericMethod(typeof(TResponse)).Invoke(interceptor, [response, cancellation])!;

    static Task RunWithBoundedConcurrencyAsync(
        List<Func<Task>> operations,
        CancellationToken cancellation,
        Action<int>? afterAdvisoryDispatchCheckForTest = null,
        Action<int>? failureRecordedForTest = null)
        => (Task)RunWithBoundedConcurrencyMethodDefinition.Invoke(
            null,
            [operations, cancellation, afterAdvisoryDispatchCheckForTest, failureRecordedForTest])!;

    /// <summary>
    /// In-memory <see cref="PayloadStore"/> test double that tracks upload/download counts and the
    /// high-water mark of concurrently in-flight calls, and optionally introduces an artificial
    /// delay or a test-controlled upload operation.
    /// </summary>
    sealed class TrackingPayloadStore(
        TimeSpan delay = default,
        Func<string, CancellationToken, Task<string>>? uploadAsync = null,
        Func<string, CancellationToken, Task<string>>? downloadAsync = null) : PayloadStore
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

        public override async Task<string> UploadAsync(string payload, CancellationToken cancellationToken)
        {
            this.EnterCall();
            try
            {
                Interlocked.Increment(ref this.uploadCount);

                if (uploadAsync != null)
                {
                    return await uploadAsync(payload, cancellationToken);
                }

                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }

                string token = TokenPrefix + Guid.NewGuid().ToString("N");
                lock (this.gate)
                {
                    this.tokenToValue[token] = payload;
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
                Interlocked.Increment(ref this.downloadCount);

                if (downloadAsync != null)
                {
                    return await downloadAsync(token, cancellationToken);
                }

                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }

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
