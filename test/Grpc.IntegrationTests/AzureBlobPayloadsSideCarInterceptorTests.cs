// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using System.Text;
using Azure;
using FluentAssertions;
using Google.Protobuf;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Grpc.Tests;

/// <summary>
/// Focused unit tests for <see cref="AzureBlobPayloadsSideCarInterceptor"/>'s payload
/// externalization/resolution and bounded-concurrency logic. These tests invoke the interceptor's
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
    public async Task ResolveResponsePayloadsAsync_WorkItemOrchestratorRequest_ResolvesExecutionRewoundReasons()
    {
        // Arrange
        TrackingPayloadStore store = new();
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, CreateOptions());
        P.WorkItem workItem = new()
        {
            OrchestratorRequest = new P.OrchestratorRequest
            {
                InstanceId = "instance-1",
                PastEvents =
                {
                    new P.HistoryEvent
                    {
                        ExecutionRewound = new P.ExecutionRewoundEvent
                        {
                            Reason = store.Seed("previous rewind reason"),
                        },
                    },
                },
                NewEvents =
                {
                    new P.HistoryEvent
                    {
                        ExecutionRewound = new P.ExecutionRewoundEvent
                        {
                            Reason = store.Seed("current rewind reason"),
                        },
                    },
                },
            },
        };

        // Act
        await ResolveAsync(interceptor, workItem, CancellationToken.None);

        // Assert
        workItem.OrchestratorRequest.PastEvents[0].ExecutionRewound.Reason
            .Should().Be("previous rewind reason");
        workItem.OrchestratorRequest.NewEvents[0].ExecutionRewound.Reason
            .Should().Be("current rewind reason");
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalizeRequestPayloadsAsync_OrchestratorResponse_OversizedLaterPayloadDoesNotUploadOrMutateTokens(
        bool unicode)
    {
        // Arrange
        TrackingPayloadStore store = new();
        LargePayloadStorageOptions options = new() { ThresholdBytes = 1024, MaxPayloadBytes = 2048 };
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, options);
        P.OrchestratorAction action = new()
        {
            Id = 1,
            ScheduleTask = new P.ScheduleTaskAction
            {
                Name = "activity",
                Input = unicode ? new string('\u00e9', 1025) : new string('x', 2049),
            },
        };
        P.OrchestratorAction expectedAction = action.Clone();
        P.OrchestratorResponse response = new()
        {
            InstanceId = "instance-1",
            CompletionToken = "completion-token",
            CustomStatus = new string('s', 1024),
            Actions = { action },
        };

        // Act
        await ExternalizeAsync(interceptor, response, CancellationToken.None);

        // Assert
        store.UploadCount.Should().Be(0);
        action.Should().Be(expectedAction);
        response.CustomStatus.Should().BeNull();
        response.CompletionToken.Should().Be("completion-token");
        P.CompleteOrchestrationAction failure = response.Actions.Should().ContainSingle().Subject.CompleteOrchestration;
        failure.OrchestrationStatus.Should().Be(P.OrchestrationStatus.Failed);
        failure.FailureDetails.ErrorType.Should().Be(typeof(PayloadStorageException).FullName);
        failure.FailureDetails.IsNonRetriable.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalizeRequestPayloadsAsync_EntityBatchRequest_OversizedLaterPayloadDoesNotUploadOrMutateTokens(
        bool unicode)
    {
        // Arrange
        TrackingPayloadStore store = new();
        LargePayloadStorageOptions options = new() { ThresholdBytes = 1024, MaxPayloadBytes = 2048 };
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, options);
        P.EntityBatchRequest request = new()
        {
            InstanceId = "entity-1",
            EntityState = new string('s', 1024),
            Operations =
            {
                new P.OperationRequest { Operation = "first", Input = new string('x', 2048) },
                new P.OperationRequest
                {
                    Operation = "second",
                    Input = unicode ? new string('\u00e9', 1025) : new string('x', 2049),
                },
            },
        };
        P.EntityBatchRequest expected = request.Clone();

        // Act
        Func<Task> act = () => ExternalizeAsync(interceptor, request, CancellationToken.None);

        // Assert
        await act.Should().ThrowExactlyAsync<PayloadStorageException>()
            .WithMessage("*exceeds the configured maximum*");
        store.UploadCount.Should().Be(0);
        request.Should().Be(expected);
    }

    [Theory]
    [InlineData("result")]
    [InlineData("details")]
    [InlineData("termination")]
    [InlineData("sub-orchestration")]
    [InlineData("event")]
    [InlineData("entity-signal")]
    [InlineData("entity-call")]
    public async Task ExternalizeRequestPayloadsAsync_OrchestratorResponse_OversizedActionPreflightsEntireMessage(
        string field)
    {
        // Arrange
        string oversized = new('x', 13);
        P.OrchestratorAction action = field switch
        {
            "result" => new() { CompleteOrchestration = new P.CompleteOrchestrationAction { Result = oversized } },
            "details" => new()
            {
                CompleteOrchestration = new P.CompleteOrchestrationAction { Result = "result--", Details = oversized },
            },
            "termination" => new() { TerminateOrchestration = new P.TerminateOrchestrationAction { Reason = oversized } },
            "sub-orchestration" => new() { CreateSubOrchestration = new P.CreateSubOrchestrationAction { Input = oversized } },
            "event" => new() { SendEvent = new P.SendEventAction { Data = oversized } },
            "entity-signal" => new()
            {
                SendEntityMessage = new P.SendEntityMessageAction
                {
                    EntityOperationSignaled = new P.EntityOperationSignaledEvent { Input = oversized },
                },
            },
            "entity-call" => new()
            {
                SendEntityMessage = new P.SendEntityMessageAction
                {
                    EntityOperationCalled = new P.EntityOperationCalledEvent { Input = oversized },
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        P.OrchestratorAction expected = action.Clone();
        P.OrchestratorResponse response = new() { CustomStatus = "status--", Actions = { action } };
        TrackingPayloadStore store = new();
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, new() { ThresholdBytes = 8, MaxPayloadBytes = 12 });

        // Act
        await ExternalizeAsync(interceptor, response, CancellationToken.None);

        // Assert
        store.UploadCount.Should().Be(0);
        action.Should().Be(expected);
        P.CompleteOrchestrationAction failure = response.Actions.Should().ContainSingle().Subject.CompleteOrchestration;
        failure.OrchestrationStatus.Should().Be(P.OrchestrationStatus.Failed);
        failure.FailureDetails.ErrorType.Should().Be(typeof(PayloadStorageException).FullName);
        failure.FailureDetails.IsNonRetriable.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, P.HistoryEvent.EventTypeOneofCase.ExecutionStarted)]
    [InlineData(true, P.HistoryEvent.EventTypeOneofCase.ExecutionStarted)]
    [InlineData(false, P.HistoryEvent.EventTypeOneofCase.ExecutionCompleted)]
    [InlineData(true, P.HistoryEvent.EventTypeOneofCase.ExecutionCompleted)]
    [InlineData(false, P.HistoryEvent.EventTypeOneofCase.ExecutionTerminated)]
    [InlineData(true, P.HistoryEvent.EventTypeOneofCase.ExecutionTerminated)]
    [InlineData(false, P.HistoryEvent.EventTypeOneofCase.EventRaised)]
    [InlineData(true, P.HistoryEvent.EventTypeOneofCase.EventRaised)]
    [InlineData(false, P.HistoryEvent.EventTypeOneofCase.TaskScheduled)]
    [InlineData(true, P.HistoryEvent.EventTypeOneofCase.TaskScheduled)]
    [InlineData(false, P.HistoryEvent.EventTypeOneofCase.TaskCompleted)]
    [InlineData(true, P.HistoryEvent.EventTypeOneofCase.TaskCompleted)]
    [InlineData(false, P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCreated)]
    [InlineData(true, P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCreated)]
    [InlineData(false, P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCompleted)]
    [InlineData(true, P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCompleted)]
    [InlineData(false, P.HistoryEvent.EventTypeOneofCase.EventSent)]
    [InlineData(true, P.HistoryEvent.EventTypeOneofCase.EventSent)]
    [InlineData(false, P.HistoryEvent.EventTypeOneofCase.GenericEvent)]
    [InlineData(true, P.HistoryEvent.EventTypeOneofCase.GenericEvent)]
    [InlineData(false, P.HistoryEvent.EventTypeOneofCase.ContinueAsNew)]
    [InlineData(true, P.HistoryEvent.EventTypeOneofCase.ContinueAsNew)]
    [InlineData(false, P.HistoryEvent.EventTypeOneofCase.ExecutionSuspended)]
    [InlineData(true, P.HistoryEvent.EventTypeOneofCase.ExecutionSuspended)]
    [InlineData(false, P.HistoryEvent.EventTypeOneofCase.ExecutionResumed)]
    [InlineData(true, P.HistoryEvent.EventTypeOneofCase.ExecutionResumed)]
    [InlineData(false, P.HistoryEvent.EventTypeOneofCase.ExecutionRewound)]
    [InlineData(true, P.HistoryEvent.EventTypeOneofCase.ExecutionRewound)]
    [InlineData(false, P.HistoryEvent.EventTypeOneofCase.EntityOperationSignaled)]
    [InlineData(true, P.HistoryEvent.EventTypeOneofCase.EntityOperationSignaled)]
    [InlineData(false, P.HistoryEvent.EventTypeOneofCase.EntityOperationCalled)]
    [InlineData(true, P.HistoryEvent.EventTypeOneofCase.EntityOperationCalled)]
    [InlineData(false, P.HistoryEvent.EventTypeOneofCase.EntityOperationCompleted)]
    [InlineData(true, P.HistoryEvent.EventTypeOneofCase.EntityOperationCompleted)]
    [InlineData(false, P.HistoryEvent.EventTypeOneofCase.HistoryState)]
    [InlineData(true, P.HistoryEvent.EventTypeOneofCase.HistoryState)]
    public async Task ExternalizeRequestPayloadsAsync_OrchestratorResponse_OversizedNestedPayloadPreflightsEntireMessage(
        bool rewind,
        P.HistoryEvent.EventTypeOneofCase eventType)
    {
        // Arrange
        P.HistoryEvent historyEvent = CreateHistoryEventWithPayload(eventType, new string('x', 13));
        P.OrchestratorResponse response = CreateResponseWithHistory(historyEvent, rewind);
        response.CustomStatus = "status--";
        P.OrchestratorAction action = response.Actions[0];
        P.OrchestratorAction expected = action.Clone();
        TrackingPayloadStore store = new(uploadAsync: (_, _) => throw new RequestFailedException(500, "Upload must not start."));
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, new() { ThresholdBytes = 8, MaxPayloadBytes = 12 });

        // Act
        await ExternalizeAsync(interceptor, response, CancellationToken.None);

        // Assert: deterministic size failure precedes even a hypothetical earlier I/O failure.
        store.UploadCount.Should().Be(0);
        action.Should().Be(expected);
        P.CompleteOrchestrationAction failure = response.Actions.Should().ContainSingle().Subject.CompleteOrchestration;
        failure.OrchestrationStatus.Should().Be(P.OrchestrationStatus.Failed);
        failure.FailureDetails.ErrorType.Should().Be(typeof(PayloadStorageException).FullName);
        failure.FailureDetails.IsNonRetriable.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExternalizeRequestPayloadsAsync_OrchestratorResponse_OversizedLaterHistoryStateFieldDoesNotMutateTokens(
        bool rewind,
        bool oversizedStatus)
    {
        // Arrange
        P.HistoryEvent historyEvent = new()
        {
            HistoryState = new P.HistoryStateEvent
            {
                OrchestrationState = new P.OrchestrationState
                {
                    Input = "input---",
                    Output = oversizedStatus ? "output--" : new string('x', 13),
                    CustomStatus = oversizedStatus ? new string('x', 13) : "status--",
                },
            },
        };
        P.HistoryEvent expected = historyEvent.Clone();
        P.OrchestratorResponse response = CreateResponseWithHistory(historyEvent, rewind);
        TrackingPayloadStore store = new();
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, new() { ThresholdBytes = 8, MaxPayloadBytes = 12 });

        // Act
        await ExternalizeAsync(interceptor, response, CancellationToken.None);

        // Assert
        store.UploadCount.Should().Be(0);
        historyEvent.Should().Be(expected);
        response.Actions.Should().ContainSingle().Which.CompleteOrchestration.OrchestrationStatus
            .Should().Be(P.OrchestrationStatus.Failed);
    }

    [Theory]
    [InlineData("result", false)]
    [InlineData("result", true)]
    [InlineData("signal", false)]
    [InlineData("signal", true)]
    [InlineData("start", false)]
    [InlineData("start", true)]
    public async Task ExternalizeRequestPayloadsAsync_EntityBatchResult_OversizedLaterPayloadDoesNotUploadOrMutateTokens(
        string field,
        bool unicode)
    {
        // Arrange
        string oversized = unicode ? new string('\u00e9', 7) : new string('x', 13);
        P.EntityBatchResult result = new()
        {
            EntityState = "state---",
            Results =
            {
                new P.OperationResult
                {
                    Success = new P.OperationResultSuccess { Result = field == "result" ? oversized : "result--" },
                },
            },
            Actions =
            {
                new P.OperationAction
                {
                    SendSignal = new P.SendSignalAction { Input = field == "signal" ? oversized : "signal--" },
                },
                new P.OperationAction
                {
                    StartNewOrchestration = new P.StartNewOrchestrationAction { Input = field == "start" ? oversized : "start---" },
                },
            },
        };
        P.EntityBatchResult expected = result.Clone();
        TrackingPayloadStore store = new();
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, new() { ThresholdBytes = 8, MaxPayloadBytes = 12 });

        // Act
        Func<Task> act = () => ExternalizeAsync(interceptor, result, CancellationToken.None);

        // Assert
        await act.Should().ThrowExactlyAsync<PayloadStorageException>();
        store.UploadCount.Should().Be(0);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("client")]
    [InlineData("activity")]
    [InlineData("orchestration")]
    [InlineData("entity-request")]
    [InlineData("entity-result")]
    [InlineData("entity-v2")]
    [InlineData("carryover")]
    [InlineData("rewind")]
    public async Task ExternalizeRequestPayloadsAsync_PreCancelledOversizedPayloadPreservesMessage(
        string messageKind)
    {
        // Arrange
        string oversized = new('x', 13);
        IMessage request = messageKind switch
        {
            "client" => new P.CreateInstanceRequest { Input = oversized },
            "activity" => new P.ActivityResponse { Result = oversized },
            "orchestration" => new P.OrchestratorResponse { CustomStatus = oversized },
            "entity-request" => new P.EntityBatchRequest
            {
                EntityState = oversized,
                Operations = { new P.OperationRequest { Input = "input---" } },
            },
            "entity-result" => new P.EntityBatchResult { EntityState = oversized },
            "entity-v2" => new P.EntityRequest { EntityState = oversized },
            "carryover" => CreateResponseWithHistory(
                CreateHistoryEventWithPayload(P.HistoryEvent.EventTypeOneofCase.ExecutionRewound, oversized),
                rewind: false),
            "rewind" => CreateResponseWithHistory(
                CreateHistoryEventWithPayload(P.HistoryEvent.EventTypeOneofCase.HistoryState, oversized),
                rewind: true),
            _ => throw new ArgumentOutOfRangeException(nameof(messageKind)),
        };
        ByteString expected = request.ToByteString();
        TrackingPayloadStore store = new();
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, new() { ThresholdBytes = 8, MaxPayloadBytes = 12 });
        using CancellationTokenSource cts = new();
        cts.Cancel();

        // Act
        Func<Task> act = () => ExternalizeAsync(interceptor, request, cts.Token);

        // Assert
        (await act.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(cts.Token);
        store.UploadCount.Should().Be(0);
        request.ToByteString().Should().Equal(expected);
    }

    [Theory]
    [InlineData("client")]
    [InlineData("activity")]
    [InlineData("orchestration")]
    [InlineData("entity-request")]
    [InlineData("entity-result")]
    [InlineData("entity-v2")]
    public async Task ExternalizeRequestPayloadsAsync_PreCancelledIneligiblePayloadsIgnoreMaximumAndPerformNoWork(
        string messageKind)
    {
        // Arrange: inline values exceed the maximum but not the externalization threshold.
        TrackingPayloadStore store = new();
        string token = store.Seed("original");
        IMessage request = messageKind switch
        {
            "client" => new P.CreateInstanceRequest { Input = "inline" },
            "activity" => new P.ActivityResponse { Result = token },
            "orchestration" => new P.OrchestratorResponse
            {
                CustomStatus = "inline",
                Actions =
                {
                    new P.OrchestratorAction
                    {
                        CompleteOrchestration = new P.CompleteOrchestrationAction
                        {
                            Result = token,
                            Details = string.Empty,
                            CarryoverEvents =
                            {
                                new P.HistoryEvent
                                {
                                    HistoryState = new P.HistoryStateEvent
                                    {
                                        OrchestrationState = new P.OrchestrationState { Output = "inline", CustomStatus = token },
                                    },
                                },
                            },
                        },
                    },
                    new P.OrchestratorAction
                    {
                        RewindOrchestration = new P.RewindOrchestrationAction
                        {
                            NewHistory =
                            {
                                new P.HistoryEvent { EventRaised = new P.EventRaisedEvent { Input = string.Empty } },
                                new P.HistoryEvent { TimerFired = new P.TimerFiredEvent() },
                            },
                        },
                    },
                },
            },
            "entity-request" => new P.EntityBatchRequest
            {
                EntityState = "inline",
                Operations = { new P.OperationRequest { Input = token }, new P.OperationRequest() },
            },
            "entity-result" => new P.EntityBatchResult
            {
                EntityState = "inline",
                Results = { new P.OperationResult { Success = new P.OperationResultSuccess { Result = token } } },
            },
            "entity-v2" => new P.EntityRequest(),
            _ => throw new ArgumentOutOfRangeException(nameof(messageKind)),
        };
        ByteString expected = request.ToByteString();
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, new() { ThresholdBytes = 8, MaxPayloadBytes = 4 });
        using CancellationTokenSource cts = new();
        cts.Cancel();

        // Act
        await ExternalizeAsync(interceptor, request, cts.Token);

        // Assert
        store.UploadCount.Should().Be(0);
        store.DownloadCount.Should().Be(0);
        request.ToByteString().Should().Equal(expected);
    }

    [Theory]
    [InlineData("orchestration", false)]
    [InlineData("orchestration", true)]
    [InlineData("entity-request", false)]
    [InlineData("entity-request", true)]
    [InlineData("entity-result", false)]
    [InlineData("entity-result", true)]
    public async Task ExternalizeRequestPayloadsAsync_AllValidBatchHonorsUtf8BoundariesAndExistingTokens(
        string messageKind,
        bool unicode)
    {
        // Arrange
        string threshold = unicode ? new string('\u00e9', 4) : new string('t', 8);
        string maximum = unicode ? new string('\u00e9', 6) : new string('m', 12);
        TrackingPayloadStore store = new();
        string token = store.Seed("original");
        IMessage request = messageKind switch
        {
            "orchestration" => new P.OrchestratorResponse
            {
                CustomStatus = threshold,
                Actions =
                {
                    new P.OrchestratorAction { ScheduleTask = new P.ScheduleTaskAction { Input = maximum } },
                    new P.OrchestratorAction { SendEvent = new P.SendEventAction { Data = "inline" } },
                    new P.OrchestratorAction { CompleteOrchestration = new P.CompleteOrchestrationAction { Result = token } },
                },
            },
            "entity-request" => new P.EntityBatchRequest
            {
                EntityState = threshold,
                Operations =
                {
                    new P.OperationRequest { Input = maximum },
                    new P.OperationRequest { Input = "inline" },
                    new P.OperationRequest { Input = token },
                    new P.OperationRequest { Input = string.Empty },
                    new P.OperationRequest(),
                },
            },
            "entity-result" => new P.EntityBatchResult
            {
                EntityState = threshold,
                Results =
                {
                    new P.OperationResult { Success = new P.OperationResultSuccess { Result = maximum } },
                    new P.OperationResult { Success = new P.OperationResultSuccess { Result = "inline" } },
                    new P.OperationResult { Success = new P.OperationResultSuccess { Result = token } },
                    new P.OperationResult { Success = new P.OperationResultSuccess { Result = string.Empty } },
                    new P.OperationResult { Success = new P.OperationResultSuccess() },
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(messageKind)),
        };
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, new() { ThresholdBytes = 8, MaxPayloadBytes = 12 });

        // Act
        await ExternalizeAsync(interceptor, request, CancellationToken.None);
        await ExternalizeAsync(interceptor, request, CancellationToken.None);

        // Assert: exactly two token substitutions, including after a retry.
        (string? first, string? second, string? inline, string? known) = request switch
        {
            P.OrchestratorResponse r => (r.CustomStatus, r.Actions[0].ScheduleTask.Input, r.Actions[1].SendEvent.Data, r.Actions[2].CompleteOrchestration.Result),
            P.EntityBatchRequest r => (r.EntityState, r.Operations[0].Input, r.Operations[1].Input, r.Operations[2].Input),
            P.EntityBatchResult r => (r.EntityState, r.Results[0].Success.Result, r.Results[1].Success.Result, r.Results[2].Success.Result),
            _ => throw new InvalidOperationException(),
        };
        store.UploadCount.Should().Be(2);
        store.GetUploadedValue(Assert.IsType<string>(first)).Should().Be(threshold);
        store.GetUploadedValue(Assert.IsType<string>(second)).Should().Be(maximum);
        inline.Should().Be("inline");
        known.Should().Be(token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalizeRequestPayloadsAsync_OrchestratorResponse_UsesNestedFieldSnapshotsBeforeDispatch(
        bool rewind)
    {
        // Arrange
        TaskCompletionSource<bool> firstWaveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> releaseUploads = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int uploadsStarted = 0;
        P.OrchestrationState state = new() { Input = "input---", Output = "output--", CustomStatus = "status--" };
        P.HistoryEvent historyEvent = new() { HistoryState = new P.HistoryStateEvent { OrchestrationState = state } };
        P.OrchestratorResponse response = CreateResponseWithHistory(historyEvent, rewind);
        response.CustomStatus = "first---";
        for (int i = 0; i < 7; i++)
        {
            response.Actions.Insert(i, new P.OrchestratorAction
            {
                ScheduleTask = new P.ScheduleTaskAction { Input = $"input--{i}" },
            });
        }

        TrackingPayloadStore store = new(uploadAsync: async (payload, cancellationToken) =>
        {
            if (Interlocked.Increment(ref uploadsStarted) == 8)
            {
                firstWaveStarted.TrySetResult(true);
            }

            await releaseUploads.Task.WaitAsync(cancellationToken);
            return $"test-blob://{payload}";
        });
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, new() { ThresholdBytes = 8, MaxPayloadBytes = 12 });

        // Act: nested fields are queued behind the eight in-flight uploads.
        Task externalizeTask = ExternalizeAsync(interceptor, response, CancellationToken.None);
        try
        {
            await firstWaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            state.Output = new string('x', 13);
            state.CustomStatus = new string('y', 13);
        }
        finally
        {
            releaseUploads.TrySetResult(true);
            await externalizeTask.WaitAsync(TimeSpan.FromSeconds(10));
        }

        // Assert
        store.UploadCount.Should().Be(11);
        store.MaxObservedConcurrency.Should().Be(8);
        response.CustomStatus.Should().Be("test-blob://first---");
        state.Input.Should().Be("test-blob://input---");
        state.Output.Should().Be("test-blob://output--");
        state.CustomStatus.Should().Be("test-blob://status--");
        for (int i = 0; i < 7; i++)
        {
            response.Actions[i].ScheduleTask.Input.Should().Be($"test-blob://input--{i}");
        }
    }

    [Theory]
    [InlineData("orchestration")]
    [InlineData("carryover")]
    [InlineData("rewind")]
    [InlineData("entity-request")]
    [InlineData("entity-result")]
    public async Task ExternalizeRequestPayloadsAsync_IndependentFieldsDoNotAcquireMessageMonitors(string messageKind)
    {
        // Arrange
        IMessage request;
        object[] messages;
        switch (messageKind)
        {
            case "orchestration":
                P.OrchestratorResponse response = new()
                {
                    CustomStatus = "payload",
                    Actions = { new P.OrchestratorAction { ScheduleTask = new P.ScheduleTaskAction { Input = "payload" } } },
                };
                request = response;
                messages = [response, response.Actions[0].ScheduleTask];
                break;
            case "carryover":
            case "rewind":
                P.HistoryEvent historyEvent = CreateHistoryEventWithPayload(
                    P.HistoryEvent.EventTypeOneofCase.HistoryState,
                    "payload");
                P.OrchestratorResponse nestedResponse = CreateResponseWithHistory(historyEvent, messageKind == "rewind");
                request = nestedResponse;
                messages = [nestedResponse, historyEvent.HistoryState.OrchestrationState];
                break;
            case "entity-request":
                P.EntityBatchRequest batch = new()
                {
                    EntityState = "payload",
                    Operations = { new P.OperationRequest { Input = "payload" } },
                };
                request = batch;
                messages = [batch, batch.Operations[0]];
                break;
            case "entity-result":
                P.EntityBatchResult result = new()
                {
                    EntityState = "payload",
                    Results = { new P.OperationResult { Success = new P.OperationResultSuccess { Result = "payload" } } },
                    Actions =
                    {
                        new P.OperationAction { SendSignal = new P.SendSignalAction { Input = "payload" } },
                        new P.OperationAction { StartNewOrchestration = new P.StartNewOrchestrationAction { Input = "payload" } },
                    },
                };
                request = result;
                messages = [result, result.Results[0].Success, result.Actions[0].SendSignal, result.Actions[1].StartNewOrchestration];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(messageKind));
        }

        TrackingPayloadStore store = new();
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, CreateOptions());
        TaskCompletionSource<bool> messageLocksHeld = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using ManualResetEventSlim releaseMessageLocks = new();
        Task holdMessageLocks = Task.Factory.StartNew(
            () =>
            {
                int acquired = 0;
                try
                {
                    foreach (object message in messages)
                    {
                        Monitor.Enter(message);
                        acquired++;
                    }

                    messageLocksHeld.SetResult(true);
                    releaseMessageLocks.Wait();
                }
                finally
                {
                    for (int i = acquired - 1; i >= 0; i--)
                    {
                        Monitor.Exit(messages[i]);
                    }
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        Task externalizeTask = Task.CompletedTask;

        // Act: finish externalization while another thread still owns the message monitors.
        try
        {
            await messageLocksHeld.Task.WaitAsync(TimeSpan.FromSeconds(10));
            externalizeTask = Task.Run(() => ExternalizeAsync(interceptor, request, CancellationToken.None));
            await externalizeTask.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            releaseMessageLocks.Set();
            await holdMessageLocks.WaitAsync(TimeSpan.FromSeconds(10));
            await externalizeTask.WaitAsync(TimeSpan.FromSeconds(10));
        }

        // Assert
        IReadOnlyList<string> payloads = request switch
        {
            P.OrchestratorResponse r when messageKind == "orchestration" =>
                [r.CustomStatus, r.Actions[0].ScheduleTask.Input],
            P.OrchestratorResponse r => GetHistoryEventPayloads(
                r.Actions[0].CompleteOrchestration?.CarryoverEvents[0] ?? r.Actions[0].RewindOrchestration.NewHistory[0]),
            P.EntityBatchRequest r => [r.EntityState, r.Operations[0].Input],
            P.EntityBatchResult r =>
                [r.EntityState, r.Results[0].Success.Result, r.Actions[0].SendSignal.Input, r.Actions[1].StartNewOrchestration.Input],
            _ => throw new InvalidOperationException(),
        };
        store.UploadCount.Should().Be(payloads.Count);
        foreach (string token in payloads)
        {
            store.GetUploadedValue(token).Should().Be("payload");
        }
    }

    [Fact]
    public async Task ExternalizeRequestPayloadsAsync_CreateInstanceRequest_KnownTokenBypassesSizeLimits()
    {
        // Arrange
        TrackingPayloadStore store = new();
        string token = store.Seed("original input");
        LargePayloadStorageOptions options = new() { ThresholdBytes = 1, MaxPayloadBytes = 1 };
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, options);
        P.CreateInstanceRequest request = new() { Input = token };

        // Act
        await ExternalizeAsync(interceptor, request, CancellationToken.None);

        // Assert
        request.Input.Should().Be(token);
        store.UploadCount.Should().Be(0);
    }

    [Fact]
    public async Task ExternalizeRequestPayloadsAsync_RewindInstanceRequest_ExternalizesReason()
    {
        // Arrange
        TrackingPayloadStore store = new();
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, CreateOptions());
        P.RewindInstanceRequest request = new()
        {
            InstanceId = "instance-1",
            Reason = "rewind reason",
        };

        // Act
        await ExternalizeAsync(interceptor, request, CancellationToken.None);

        // Assert
        store.GetUploadedValue(request.Reason).Should().Be("rewind reason");
    }

    [Theory]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.ExecutionStarted)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.ExecutionCompleted)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.ExecutionTerminated)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.EventRaised)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.TaskScheduled)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.TaskCompleted)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCreated)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCompleted)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.EventSent)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.GenericEvent)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.ContinueAsNew)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.ExecutionSuspended)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.ExecutionResumed)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.ExecutionRewound)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.EntityOperationSignaled)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.EntityOperationCalled)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.EntityOperationCompleted)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.HistoryState)]
    public async Task ExternalizeRequestPayloadsAsync_OrchestratorResponse_ExternalizesRewindHistoryPayloads(
        P.HistoryEvent.EventTypeOneofCase eventType)
    {
        // Arrange
        string payload = new('x', 64);
        P.HistoryEvent historyEvent = CreateHistoryEventWithPayload(eventType, payload);
        P.OrchestratorResponse response = new()
        {
            InstanceId = "instance-1",
            Actions =
            {
                new P.OrchestratorAction
                {
                    RewindOrchestration = new P.RewindOrchestrationAction
                    {
                        NewHistory = { historyEvent },
                    },
                },
            },
        };

        TrackingPayloadStore store = new();
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, CreateOptions());

        // Act
        await ExternalizeAsync(interceptor, response, CancellationToken.None);
        await ExternalizeAsync(interceptor, response, CancellationToken.None);

        // Assert
        IReadOnlyList<string> tokens = GetHistoryEventPayloads(historyEvent);
        foreach (string token in tokens)
        {
            token.Should().NotBe(payload);
            store.GetUploadedValue(token).Should().Be(payload);
        }

        store.UploadCount.Should().Be(tokens.Count);
    }

    [Fact]
    public async Task ExternalizeRequestPayloadsAsync_OrchestratorResponse_ShrinksOversizedRewindHistory()
    {
        // Arrange: ExecutionStarted is representative because every replacement history contains
        // one; response-size handling is otherwise independent of the history event type.
        const int MaxInlineResponseSize = 4_089_446;
        string payload = new('x', MaxInlineResponseSize + 1024);
        P.HistoryEvent historyEvent = CreateHistoryEventWithPayload(
            P.HistoryEvent.EventTypeOneofCase.ExecutionStarted,
            payload);
        P.OrchestratorResponse response = new()
        {
            InstanceId = "instance-1",
            Actions =
            {
                new P.OrchestratorAction
                {
                    RewindOrchestration = new P.RewindOrchestrationAction
                    {
                        NewHistory = { historyEvent },
                    },
                },
            },
        };
        response.CalculateSize().Should().BeGreaterThan(MaxInlineResponseSize);

        TrackingPayloadStore store = new();
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, CreateOptions());

        // Act
        await ExternalizeAsync(interceptor, response, CancellationToken.None);

        // Assert
        store.GetUploadedValue(historyEvent.ExecutionStarted.Input).Should().Be(payload);
        response.CalculateSize().Should().BeLessThan(MaxInlineResponseSize);
    }

    [Theory]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.ExecutionStarted)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.ExecutionCompleted)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.ExecutionTerminated)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.EventRaised)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.TaskScheduled)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.TaskCompleted)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCreated)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCompleted)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.EventSent)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.GenericEvent)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.ContinueAsNew)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.ExecutionSuspended)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.ExecutionResumed)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.ExecutionRewound)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.EntityOperationSignaled)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.EntityOperationCalled)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.EntityOperationCompleted)]
    [InlineData(P.HistoryEvent.EventTypeOneofCase.HistoryState)]
    public async Task ExternalizeRequestPayloadsAsync_OrchestratorResponse_ExternalizesCarryoverHistoryPayloads(
        P.HistoryEvent.EventTypeOneofCase eventType)
    {
        // Arrange
        string payload = new('x', 64);
        P.HistoryEvent historyEvent = CreateHistoryEventWithPayload(eventType, payload);
        historyEvent.EventId = 42;
        historyEvent.Timestamp = new() { Seconds = 1_700_000_000, Nanos = 123 };
        P.OrchestratorResponse response = new()
        {
            InstanceId = "instance-1",
            CompletionToken = "completion-token",
            CustomStatus = "inline-status",
            NumEventsProcessed = 3,
            Actions =
            {
                new P.OrchestratorAction
                {
                    Id = 17,
                    CompleteOrchestration = new P.CompleteOrchestrationAction
                    {
                        OrchestrationStatus = P.OrchestrationStatus.ContinuedAsNew,
                        Result = "next-input",
                        Details = "inline-details",
                        NewVersion = "v2",
                        Tags = { ["tenant"] = "test" },
                        CarryoverEvents =
                        {
                            historyEvent,
                            new P.HistoryEvent
                            {
                                EventId = 43,
                                EventRaised = new P.EventRaisedEvent { Name = "inline", Input = "small" },
                            },
                            new P.HistoryEvent
                            {
                                EventId = 44,
                                TimerFired = new P.TimerFiredEvent { TimerId = 7 },
                            },
                        },
                    },
                },
            },
        };
        P.OrchestratorResponse expected = response.Clone();
        TrackingPayloadStore store = new();
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, new() { ThresholdBytes = 64 });

        // Act
        await ExternalizeAsync(interceptor, response, CancellationToken.None);

        // Assert
        IReadOnlyList<string> tokens = GetHistoryEventPayloads(historyEvent);
        foreach (string token in tokens)
        {
            token.Should().NotBe(payload);
            store.GetUploadedValue(token).Should().Be(payload);
        }

        store.UploadCount.Should().Be(tokens.Count);
        P.OrchestratorResponse roundTripped = P.OrchestratorResponse.Parser.ParseFrom(response.ToByteArray());
        P.WorkItem workItem = new()
        {
            OrchestratorRequest = new P.OrchestratorRequest
            {
                NewEvents = { roundTripped.Actions[0].CompleteOrchestration.CarryoverEvents },
            },
        };
        await ResolveAsync(interceptor, workItem, CancellationToken.None);

        roundTripped.Should().Be(expected);
        store.DownloadCount.Should().Be(tokens.Count);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("xxxxxxx", 7)]
    [InlineData("xxxxxxxx", 8)]
    [InlineData("xxxxxxxxx", 9)]
    [InlineData("xxxxxxxxxxx", 11)]
    [InlineData("xxxxxxxxxxxx", 12)]
    [InlineData("xxxxxxxxxxxxx", 13)]
    [InlineData("\u00e9\u00e9\u00e9x", 7)]
    [InlineData("\u00e9\u00e9\u00e9\u00e9", 8)]
    [InlineData("\u00e9\u00e9\u00e9\u00e9x", 9)]
    [InlineData("\u00e9\u00e9\u00e9\u00e9\u00e9x", 11)]
    [InlineData("\u00e9\u00e9\u00e9\u00e9\u00e9\u00e9", 12)]
    [InlineData("\u00e9\u00e9\u00e9\u00e9\u00e9\u00e9x", 13)]
    [InlineData("\U0001f600\U0001f600", 8)]
    [InlineData("\U0001f600\U0001f600\U0001f600", 12)]
    public async Task ExternalizeRequestPayloadsAsync_OrchestratorResponse_CarryoverHonorsUtf8SizeBoundaries(
        string? payload,
        int payloadBytes)
    {
        // Arrange
        LargePayloadStorageOptions options = new() { ThresholdBytes = 8, MaxPayloadBytes = 12 };
        Encoding.UTF8.GetByteCount(payload ?? string.Empty).Should().Be(payloadBytes);
        TrackingPayloadStore store = new();
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, options);
        P.HistoryEvent historyEvent = new()
        {
            EventId = 7,
            EventRaised = new P.EventRaisedEvent { Name = "buffered", Input = payload },
        };
        P.OrchestratorResponse response = new()
        {
            InstanceId = "instance-1",
            CompletionToken = "completion-token",
            CustomStatus = "status",
            Actions =
            {
                new P.OrchestratorAction
                {
                    Id = 17,
                    CompleteOrchestration = new P.CompleteOrchestrationAction
                    {
                        OrchestrationStatus = P.OrchestrationStatus.ContinuedAsNew,
                        Result = "next",
                        NewVersion = "v2",
                        CarryoverEvents = { historyEvent },
                    },
                },
            },
        };
        P.OrchestratorResponse expected = response.Clone();

        // Act
        await ExternalizeAsync(interceptor, response, CancellationToken.None);

        // Assert
        response.CompletionToken.Should().Be(expected.CompletionToken);
        response.Actions.Should().ContainSingle();
        if (payloadBytes > options.MaxPayloadBytes)
        {
            P.CompleteOrchestrationAction failure = response.Actions[0].CompleteOrchestration;
            failure.OrchestrationStatus.Should().Be(P.OrchestrationStatus.Failed);
            failure.FailureDetails.ErrorType.Should().Be(typeof(PayloadStorageException).FullName);
            failure.FailureDetails.ErrorMessage.Should().Contain("exceeds the configured maximum");
            failure.FailureDetails.IsNonRetriable.Should().BeTrue();
            failure.CarryoverEvents.Should().BeEmpty();
            failure.Result.Should().BeNull();
            response.CustomStatus.Should().BeNull();
            store.UploadCount.Should().Be(0);
        }
        else
        {
            bool externalized = payloadBytes >= options.ThresholdBytes;
            if (externalized)
            {
                string token = Assert.IsType<string>(historyEvent.EventRaised.Input);
                store.GetUploadedValue(token).Should().Be(payload);
                expected.Actions[0].CompleteOrchestration.CarryoverEvents[0].EventRaised.Input = token;
            }

            response.Should().Be(expected);
            store.UploadCount.Should().Be(externalized ? 1 : 0);
        }
    }

    [Fact]
    public async Task ExternalizeRequestPayloadsAsync_OrchestratorResponse_ShrinksNativeCarryoverAndHydratesAfterProtobufRoundTrip()
    {
        // Arrange: native carryover events, not self-targeted SendEvent actions.
        const int EventCount = 5;
        const int PayloadBytes = 1024 * 1024;
        const int MaxResponseBytes = 4 * 1024 * 1024;
        TrackingPayloadStore store = new(delay: TimeSpan.FromMilliseconds(50));
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, new LargePayloadStorageOptions());
        P.CompleteOrchestrationAction complete = new()
        {
            OrchestrationStatus = P.OrchestrationStatus.ContinuedAsNew,
            Result = "\"next-input\"",
            Details = "completion-details",
            NewVersion = "v2",
            Tags = { ["tenant"] = "test" },
        };
        for (int i = 0; i < EventCount; i++)
        {
            string payload = "\"" + new string((char)('a' + i), PayloadBytes - 2) + "\"";
            Encoding.UTF8.GetByteCount(payload).Should().Be(PayloadBytes);
            complete.CarryoverEvents.Add(new P.HistoryEvent
            {
                EventId = i,
                Timestamp = new() { Seconds = 1_700_000_000, Nanos = i },
                EventRaised = new P.EventRaisedEvent { Name = $"buffered-{i}", Input = payload },
            });
        }

        P.OrchestratorResponse response = new()
        {
            InstanceId = "carryover-repro",
            CompletionToken = "completion-token",
            CustomStatus = "inline-status",
            NumEventsProcessed = EventCount,
            Actions = { new P.OrchestratorAction { Id = 17, CompleteOrchestration = complete } },
        };
        P.OrchestratorResponse expected = response.Clone();
        response.CalculateSize().Should().BeGreaterThan(MaxResponseBytes);

        // Act
        await ExternalizeAsync(interceptor, response, CancellationToken.None);

        // Assert
        store.UploadCount.Should().Be(EventCount);
        response.CalculateSize().Should().BeLessThan(MaxResponseBytes);
        store.MaxObservedConcurrency.Should().BeGreaterThan(1);
        store.MaxObservedConcurrency.Should().BeLessOrEqualTo(8);
        P.OrchestratorResponse roundTripped = P.OrchestratorResponse.Parser.ParseFrom(response.ToByteArray());
        P.WorkItem workItem = new()
        {
            OrchestratorRequest = new P.OrchestratorRequest
            {
                InstanceId = response.InstanceId,
                NewEvents = { roundTripped.Actions[0].CompleteOrchestration.CarryoverEvents },
            },
        };
        await ResolveAsync(interceptor, workItem, CancellationToken.None);

        roundTripped.Should().Be(expected);
        store.DownloadCount.Should().Be(EventCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    public async Task ExternalizeRequestPayloadsAsync_OrchestratorResponse_PreCancelledCarryoverDoesNotUploadOrConvertResponse(
        int eventCount)
    {
        // Arrange
        TrackingPayloadStore store = new(uploadAsync: (_, _) => throw new PayloadStorageException("Upload should not start."));
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, CreateOptions());
        P.CompleteOrchestrationAction complete = new() { OrchestrationStatus = P.OrchestrationStatus.ContinuedAsNew };
        for (int i = 0; i < eventCount; i++)
        {
            complete.CarryoverEvents.Add(new P.HistoryEvent
            {
                EventId = i,
                EventRaised = new P.EventRaisedEvent { Name = "buffered", Input = $"payload-{i}" },
            });
        }

        P.OrchestratorResponse response = new()
        {
            InstanceId = "instance-1",
            CompletionToken = "completion-token",
            Actions = { new P.OrchestratorAction { CompleteOrchestration = complete } },
        };
        P.OrchestratorResponse expected = response.Clone();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        // Act
        Func<Task> act = () => ExternalizeAsync(interceptor, response, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        store.UploadCount.Should().Be(0);
        response.Should().Be(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalizeRequestPayloadsAsync_OrchestratorResponse_NestedHistoryCancellationDrainsBoundedUploads(
        bool rewind)
    {
        // Arrange
        TaskCompletionSource<bool> firstWaveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> releaseUploads = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cts = new();
        int started = 0;
        int finished = 0;
        TrackingPayloadStore store = new(uploadAsync: async (payload, cancellationToken) =>
        {
            cancellationToken.Should().Be(cts.Token);
            if (Interlocked.Increment(ref started) == 8)
            {
                firstWaveStarted.SetResult(true);
            }

            await releaseUploads.Task;
            Interlocked.Increment(ref finished);
            return $"test-blob://{payload}";
        });
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, CreateOptions());
        P.CompleteOrchestrationAction complete = new() { OrchestrationStatus = P.OrchestrationStatus.ContinuedAsNew };
        for (int i = 0; i < 16; i++)
        {
            complete.CarryoverEvents.Add(new P.HistoryEvent
            {
                EventId = i,
                EventRaised = new P.EventRaisedEvent { Name = "buffered", Input = $"payload-{i}" },
            });
        }

        P.OrchestratorResponse response = new()
        {
            InstanceId = "instance-1",
            CompletionToken = "completion-token",
            Actions =
            {
                rewind
                    ? new P.OrchestratorAction
                    {
                        RewindOrchestration = new P.RewindOrchestrationAction { NewHistory = { complete.CarryoverEvents } },
                    }
                    : new P.OrchestratorAction { CompleteOrchestration = complete },
            },
        };
        P.OrchestratorAction originalAction = response.Actions[0];

        // Act
        Task externalizeTask = ExternalizeAsync(interceptor, response, cts.Token);
        try
        {
            await firstWaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cts.Cancel();
            externalizeTask.IsCompleted.Should().BeFalse("in-flight uploads must drain even if they ignore cancellation");
        }
        finally
        {
            releaseUploads.TrySetResult(true);
        }

        // Assert
        Func<Task> act = () => externalizeTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
        store.UploadCount.Should().Be(8);
        store.MaxObservedConcurrency.Should().Be(8);
        finished.Should().Be(8);
        response.Actions.Should().ContainSingle();
        response.Actions[0].Should().BeSameAs(originalAction);
        complete.OrchestrationStatus.Should().Be(P.OrchestrationStatus.ContinuedAsNew);
        response.CompletionToken.Should().Be("completion-token");
        for (int i = 0; i < complete.CarryoverEvents.Count; i++)
        {
            complete.CarryoverEvents[i].EventRaised.Input.Should().Be(i < 8 ? $"test-blob://payload-{i}" : $"payload-{i}");
        }
    }

    [Theory]
    [InlineData(403, false)]
    [InlineData(403, true)]
    [InlineData(500, false)]
    [InlineData(500, true)]
    public async Task ExternalizeRequestPayloadsAsync_OrchestratorResponse_NestedHistoryFailureDrainsBeforeConvertingOrRethrowing(
        int statusCode,
        bool rewind)
    {
        // Arrange
        TaskCompletionSource<bool> failureStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> releaseUpload = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int finished = 0;
        TrackingPayloadStore store = new(uploadAsync: async (payload, cancellationToken) =>
        {
            try
            {
                if (payload == "slow")
                {
                    await releaseUpload.Task.WaitAsync(cancellationToken);
                    return "test-blob://slow";
                }

                failureStarted.SetResult(true);
                throw new RequestFailedException(statusCode, "Simulated carryover upload failure.");
            }
            finally
            {
                Interlocked.Increment(ref finished);
            }
        });
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, CreateOptions());
        P.CompleteOrchestrationAction complete = new()
        {
            OrchestrationStatus = P.OrchestrationStatus.ContinuedAsNew,
            CarryoverEvents =
            {
                new P.HistoryEvent { EventRaised = new P.EventRaisedEvent { Input = "slow" } },
                new P.HistoryEvent { EventRaised = new P.EventRaisedEvent { Input = "failure" } },
                new P.HistoryEvent { EventRaised = new P.EventRaisedEvent { Input = "queued" } },
            },
        };
        P.OrchestratorResponse response = new()
        {
            InstanceId = "instance-1",
            CompletionToken = "completion-token",
            Actions =
            {
                rewind
                    ? new P.OrchestratorAction
                    {
                        RewindOrchestration = new P.RewindOrchestrationAction { NewHistory = { complete.CarryoverEvents } },
                    }
                    : new P.OrchestratorAction { CompleteOrchestration = complete },
            },
        };
        P.OrchestratorAction originalAction = response.Actions[0];

        // Act
        Task externalizeTask = ExternalizeAsync(interceptor, response, CancellationToken.None);
        try
        {
            await failureStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            externalizeTask.IsCompleted.Should().BeFalse();
            response.Actions[0].Should().BeSameAs(originalAction);
        }
        finally
        {
            releaseUpload.TrySetResult(true);
        }

        // Assert
        if (statusCode == 403)
        {
            await externalizeTask;
            P.CompleteOrchestrationAction failure = response.Actions.Should().ContainSingle().Subject.CompleteOrchestration;
            failure.OrchestrationStatus.Should().Be(P.OrchestrationStatus.Failed);
            failure.FailureDetails.ErrorType.Should().Be(typeof(RequestFailedException).FullName);
            failure.FailureDetails.ErrorMessage.Should().Be("Simulated carryover upload failure.");
            failure.FailureDetails.IsNonRetriable.Should().BeTrue();
            failure.CarryoverEvents.Should().BeEmpty();
        }
        else
        {
            Func<Task> act = () => externalizeTask;
            (await act.Should().ThrowExactlyAsync<RequestFailedException>())
                .WithMessage("Simulated carryover upload failure.");
            response.Actions[0].Should().BeSameAs(originalAction);
            complete.OrchestrationStatus.Should().Be(P.OrchestrationStatus.ContinuedAsNew);
        }

        store.UploadCount.Should().Be(2, "failure must stop dispatch before the queued carryover event");
        finished.Should().Be(2, "every started upload must finish before returning or converting the response");
        complete.CarryoverEvents[0].EventRaised.Input.Should().Be("test-blob://slow");
        complete.CarryoverEvents[2].EventRaised.Input.Should().Be("queued");
        response.CompletionToken.Should().Be("completion-token");
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
        await ExternalizeAsync(interceptor, request, CancellationToken.None);

        // Assert
        store.GetUploadedValue(request.EntityState).Should().Be("state-0");
        for (int i = 0; i < operationCount; i++)
        {
            string token = request.Operations[i].Input;
            token.Should().NotBe(expectedInputs[i]);
            store.GetUploadedValue(token).Should().Be(expectedInputs[i]);
        }

        store.UploadCount.Should().Be(operationCount + 1);
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
        await ExternalizeAsync(interceptor, result, CancellationToken.None);

        // Assert
        store.GetUploadedValue(result.EntityState).Should().Be("state-final");
        for (int i = 0; i < resultCount; i++)
        {
            store.GetUploadedValue(result.Results[i].Success.Result).Should().Be(expectedResults[i]);
        }

        store.GetUploadedValue(result.Actions[0].SendSignal.Input).Should().Be("signal-input");
        store.GetUploadedValue(result.Actions[1].StartNewOrchestration.Input).Should().Be("start-input");
        store.UploadCount.Should().Be(resultCount + 3);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalizeRequestPayloadsAsync_OrchestratorResponse_PermanentFailure_PreservesEntityUnlocks(
        bool useLegacyEntityEvents)
    {
        // Arrange
        TrackingPayloadStore store = new();
        LargePayloadStorageOptions options = new() { ThresholdBytes = 1024, MaxPayloadBytes = 2048 };
        AzureBlobPayloadsSideCarInterceptor interceptor = new(store, options);
        P.OrchestratorAction unlock = new() { Id = 7 };
        if (useLegacyEntityEvents)
        {
            unlock.SendEvent = new P.SendEventAction
            {
                Instance = new P.OrchestrationInstance { InstanceId = "@counter@one" },
                Name = "release",
                Data = """{"id":"11111111-1111-1111-1111-111111111111","parent":"instance-1"}""",
            };
        }
        else
        {
            unlock.SendEntityMessage = new P.SendEntityMessageAction
            {
                EntityUnlockSent = new P.EntityUnlockSentEvent
                {
                    CriticalSectionId = "11111111-1111-1111-1111-111111111111",
                    ParentInstanceId = "instance-1",
                    TargetInstanceId = "@counter@one",
                },
            };
        }

        P.OrchestratorResponse response = new()
        {
            InstanceId = "instance-1",
            CompletionToken = "completion-token",
            Actions =
            {
                unlock,
                new P.OrchestratorAction
                {
                    Id = 8,
                    SendEvent = new P.SendEventAction
                    {
                        Instance = new P.OrchestrationInstance { InstanceId = "another-orchestration" },
                        Name = "release",
                        Data = "discarded",
                    },
                },
                new P.OrchestratorAction
                {
                    Id = 9,
                    CompleteOrchestration = new P.CompleteOrchestrationAction
                    {
                        OrchestrationStatus = P.OrchestrationStatus.Completed,
                        Result = new string('x', 4096),
                    },
                },
            },
        };

        // Act
        await ExternalizeAsync(interceptor, response, CancellationToken.None);

        // Assert
        response.Actions.Should().HaveCount(2);
        response.Actions[0].Should().BeSameAs(unlock);
        response.Actions[1].CompleteOrchestration.OrchestrationStatus.Should().Be(P.OrchestrationStatus.Failed);
        response.Actions[1].CompleteOrchestration.FailureDetails.IsNonRetriable.Should().BeTrue();
        response.CompletionToken.Should().Be("completion-token");
        store.UploadCount.Should().Be(0);
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

    static P.OrchestratorResponse CreateResponseWithHistory(P.HistoryEvent historyEvent, bool rewind)
    {
        P.OrchestratorAction action = rewind
            ? new()
            {
                RewindOrchestration = new P.RewindOrchestrationAction { NewHistory = { historyEvent } },
            }
            : new()
            {
                CompleteOrchestration = new P.CompleteOrchestrationAction
                {
                    OrchestrationStatus = P.OrchestrationStatus.ContinuedAsNew,
                    CarryoverEvents = { historyEvent },
                },
            };
        return new()
        {
            InstanceId = "instance-1",
            CompletionToken = "completion-token",
            Actions = { action },
        };
    }

    static P.HistoryEvent CreateHistoryEventWithPayload(
        P.HistoryEvent.EventTypeOneofCase eventType,
        string payload)
    {
        return eventType switch
        {
            P.HistoryEvent.EventTypeOneofCase.ExecutionStarted =>
                new() { ExecutionStarted = new P.ExecutionStartedEvent { Input = payload } },
            P.HistoryEvent.EventTypeOneofCase.ExecutionCompleted =>
                new() { ExecutionCompleted = new P.ExecutionCompletedEvent { Result = payload } },
            P.HistoryEvent.EventTypeOneofCase.ExecutionTerminated =>
                new() { ExecutionTerminated = new P.ExecutionTerminatedEvent { Input = payload } },
            P.HistoryEvent.EventTypeOneofCase.EventRaised =>
                new() { EventRaised = new P.EventRaisedEvent { Input = payload } },
            P.HistoryEvent.EventTypeOneofCase.TaskScheduled =>
                new() { TaskScheduled = new P.TaskScheduledEvent { Input = payload } },
            P.HistoryEvent.EventTypeOneofCase.TaskCompleted =>
                new() { TaskCompleted = new P.TaskCompletedEvent { Result = payload } },
            P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCreated =>
                new()
                {
                    SubOrchestrationInstanceCreated =
                        new P.SubOrchestrationInstanceCreatedEvent { Input = payload },
                },
            P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCompleted =>
                new()
                {
                    SubOrchestrationInstanceCompleted =
                        new P.SubOrchestrationInstanceCompletedEvent { Result = payload },
                },
            P.HistoryEvent.EventTypeOneofCase.EventSent =>
                new() { EventSent = new P.EventSentEvent { Input = payload } },
            P.HistoryEvent.EventTypeOneofCase.GenericEvent =>
                new() { GenericEvent = new P.GenericEvent { Data = payload } },
            P.HistoryEvent.EventTypeOneofCase.ContinueAsNew =>
                new() { ContinueAsNew = new P.ContinueAsNewEvent { Input = payload } },
            P.HistoryEvent.EventTypeOneofCase.ExecutionSuspended =>
                new() { ExecutionSuspended = new P.ExecutionSuspendedEvent { Input = payload } },
            P.HistoryEvent.EventTypeOneofCase.ExecutionResumed =>
                new() { ExecutionResumed = new P.ExecutionResumedEvent { Input = payload } },
            P.HistoryEvent.EventTypeOneofCase.ExecutionRewound =>
                new() { ExecutionRewound = new P.ExecutionRewoundEvent { Reason = payload } },
            P.HistoryEvent.EventTypeOneofCase.EntityOperationSignaled =>
                new() { EntityOperationSignaled = new P.EntityOperationSignaledEvent { Input = payload } },
            P.HistoryEvent.EventTypeOneofCase.EntityOperationCalled =>
                new() { EntityOperationCalled = new P.EntityOperationCalledEvent { Input = payload } },
            P.HistoryEvent.EventTypeOneofCase.EntityOperationCompleted =>
                new() { EntityOperationCompleted = new P.EntityOperationCompletedEvent { Output = payload } },
            P.HistoryEvent.EventTypeOneofCase.HistoryState =>
                new()
                {
                    HistoryState = new P.HistoryStateEvent
                    {
                        OrchestrationState = new P.OrchestrationState
                        {
                            Input = payload,
                            Output = payload,
                            CustomStatus = payload,
                        },
                    },
                },
            _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, null),
        };
    }

    static IReadOnlyList<string> GetHistoryEventPayloads(P.HistoryEvent historyEvent)
    {
        return historyEvent.EventTypeCase switch
        {
            P.HistoryEvent.EventTypeOneofCase.ExecutionStarted => [historyEvent.ExecutionStarted.Input],
            P.HistoryEvent.EventTypeOneofCase.ExecutionCompleted => [historyEvent.ExecutionCompleted.Result],
            P.HistoryEvent.EventTypeOneofCase.ExecutionTerminated => [historyEvent.ExecutionTerminated.Input],
            P.HistoryEvent.EventTypeOneofCase.EventRaised => [historyEvent.EventRaised.Input],
            P.HistoryEvent.EventTypeOneofCase.TaskScheduled => [historyEvent.TaskScheduled.Input],
            P.HistoryEvent.EventTypeOneofCase.TaskCompleted => [historyEvent.TaskCompleted.Result],
            P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCreated =>
                [historyEvent.SubOrchestrationInstanceCreated.Input],
            P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCompleted =>
                [historyEvent.SubOrchestrationInstanceCompleted.Result],
            P.HistoryEvent.EventTypeOneofCase.EventSent => [historyEvent.EventSent.Input],
            P.HistoryEvent.EventTypeOneofCase.GenericEvent => [historyEvent.GenericEvent.Data],
            P.HistoryEvent.EventTypeOneofCase.ContinueAsNew => [historyEvent.ContinueAsNew.Input],
            P.HistoryEvent.EventTypeOneofCase.ExecutionSuspended => [historyEvent.ExecutionSuspended.Input],
            P.HistoryEvent.EventTypeOneofCase.ExecutionResumed => [historyEvent.ExecutionResumed.Input],
            P.HistoryEvent.EventTypeOneofCase.ExecutionRewound => [historyEvent.ExecutionRewound.Reason],
            P.HistoryEvent.EventTypeOneofCase.EntityOperationSignaled =>
                [historyEvent.EntityOperationSignaled.Input],
            P.HistoryEvent.EventTypeOneofCase.EntityOperationCalled =>
                [historyEvent.EntityOperationCalled.Input],
            P.HistoryEvent.EventTypeOneofCase.EntityOperationCompleted =>
                [historyEvent.EntityOperationCompleted.Output],
            P.HistoryEvent.EventTypeOneofCase.HistoryState =>
                [
                    historyEvent.HistoryState.OrchestrationState.Input,
                    historyEvent.HistoryState.OrchestrationState.Output,
                    historyEvent.HistoryState.OrchestrationState.CustomStatus,
                ],
            _ => throw new ArgumentOutOfRangeException(
                nameof(historyEvent),
                historyEvent.EventTypeCase,
                null),
        };
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
