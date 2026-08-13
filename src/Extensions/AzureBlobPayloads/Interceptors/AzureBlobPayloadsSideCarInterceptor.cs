// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.ExceptionServices;
using Azure;
using Grpc.Core.Interceptors;

using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask;

/// <summary>
/// gRPC interceptor that externalizes large payloads to an <see cref="PayloadStore"/> on requests
/// and resolves known payload tokens on responses for SideCar.
/// </summary>
public sealed class AzureBlobPayloadsSideCarInterceptor(PayloadStore payloadStore, LargePayloadStorageOptions options)
    : PayloadInterceptor<object, object>(payloadStore, options)
{
    // Conservative per-message cap on simultaneous Azure Blob uploads/downloads (e.g. a fan-out
    // orchestrator response, a history chunk, or a batch of entity operations). This overlaps
    // independent payload I/O without issuing an unbounded burst from one message. Account-wide
    // throughput remains governed by Azure Storage scalability targets and SDK retry behavior
    // (see https://aka.ms/azure-storage-scalability-targets).
    const int MaxConcurrentPayloadOperations = 8;

    Action? BeforeSharedMessageLockForTest { get; set; }

    Action<object>? SharedMessageLockAcquiredForTest { get; set; }

    /// <inheritdoc/>
    protected override async Task ExternalizeRequestPayloadsAsync<TRequest>(TRequest request, CancellationToken cancellation)
    {
        // Client -> sidecar
        switch (request)
        {
            case P.CreateInstanceRequest r:
                r.Input = await this.MaybeExternalizeAsync(r.Input, cancellation);
                break;
            case P.RaiseEventRequest r:
                r.Input = await this.MaybeExternalizeAsync(r.Input, cancellation);
                break;
            case P.TerminateRequest r:
                r.Output = await this.MaybeExternalizeAsync(r.Output, cancellation);
                break;
            case P.SuspendRequest r:
                r.Reason = await this.MaybeExternalizeAsync(r.Reason, cancellation);
                break;
            case P.ResumeRequest r:
                r.Reason = await this.MaybeExternalizeAsync(r.Reason, cancellation);
                break;
            case P.SignalEntityRequest r:
                r.Input = await this.MaybeExternalizeAsync(r.Input, cancellation);
                break;
            case P.ActivityResponse r:
                try
                {
                    r.Result = await this.MaybeExternalizeAsync(r.Result, cancellation);
                }
                catch (Exception ex) when (IsPermanentStorageFailure(ex))
                {
                    // Permanent failure (e.g., payload exceeds configured maximum, 4xx auth/permission error).
                    // Replace with a failure response so the orchestration sees a failed activity
                    // instead of the work item being abandoned and redelivered indefinitely.
                    r.Result = null;
                    r.FailureDetails = new P.TaskFailureDetails
                    {
                        ErrorType = ex.GetType().FullName,
                        ErrorMessage = ex.Message,
                        StackTrace = ex.StackTrace,
                        IsNonRetriable = true,
                    };
                }

                break;
            case P.OrchestratorResponse r:
                try
                {
                    await this.ExternalizeOrchestratorResponseAsync(r, cancellation);
                }
                catch (Exception ex) when (IsPermanentStorageFailure(ex))
                {
                    // Permanent failure during orchestration response externalization.
                    // Replace all actions with a single Failed completion so the orchestration
                    // terminates instead of being abandoned and redelivered indefinitely.
                    r.Actions.Clear();
                    r.CustomStatus = null;
#pragma warning disable CS0612 // isPartial/chunkIndex are deprecated but still required for chunked response wire compatibility.
                    r.IsPartial = false;
                    r.ChunkIndex = null;
#pragma warning restore CS0612
                    r.Actions.Add(new P.OrchestratorAction
                    {
                        CompleteOrchestration = new P.CompleteOrchestrationAction
                        {
                            OrchestrationStatus = P.OrchestrationStatus.Failed,
                            FailureDetails = new P.TaskFailureDetails
                            {
                                ErrorType = ex.GetType().FullName,
                                ErrorMessage = ex.Message,
                                StackTrace = ex.StackTrace,
                                IsNonRetriable = true,
                            },
                        },
                    });
                }

                break;
            case P.EntityBatchResult r:
                await this.ExternalizeEntityBatchResultAsync(r, cancellation);
                break;
            case P.EntityBatchRequest r:
                await this.ExternalizeEntityBatchRequestAsync(r, cancellation);
                break;
            case P.EntityRequest r:
                r.EntityState = await this.MaybeExternalizeAsync(r.EntityState, cancellation);
                break;
        }
    }

    /// <inheritdoc/>
    protected override async Task ResolveResponsePayloadsAsync<TResponse>(TResponse response, CancellationToken cancellation)
    {
        // Sidecar -> client/worker
        switch (response)
        {
            case P.GetInstanceResponse r when r.OrchestrationState is { } s:
                {
                    List<Func<Task>> operations = [];
                    this.AddOrchestrationStateResolutionOperations(operations, s, cancellation);
                    await RunWithBoundedConcurrencyAsync(operations, cancellation);
                }

                break;
            case P.HistoryChunk c when c.Events != null:
                {
                    List<Func<Task>> operations = [];
                    foreach (P.HistoryEvent e in c.Events)
                    {
                        if (this.RequiresEventPayloadResolution(e))
                        {
                            operations.Add(() => this.ResolveEventPayloadsAsync(e, cancellation));
                        }
                    }

                    await RunWithBoundedConcurrencyAsync(operations, cancellation);
                }

                break;
            case P.QueryInstancesResponse r:
                {
                    List<Func<Task>> operations = [];
                    foreach (P.OrchestrationState s in r.OrchestrationState)
                    {
                        this.AddOrchestrationStateResolutionOperations(operations, s, cancellation);
                    }

                    await RunWithBoundedConcurrencyAsync(operations, cancellation);
                }

                break;
            case P.GetEntityResponse r when r.Entity is { } em:
                em.SerializedState = await this.MaybeResolveAsync(em.SerializedState, cancellation);
                break;
            case P.QueryEntitiesResponse r:
                {
                    List<Func<Task>> operations = [];
                    foreach (P.EntityMetadata em in r.Entities)
                    {
                        string? serializedState = em.SerializedState;
                        if (this.RequiresResolution(serializedState))
                        {
                            operations.Add(async () => em.SerializedState = await this.MaybeResolveAsync(serializedState, cancellation));
                        }
                    }

                    await RunWithBoundedConcurrencyAsync(operations, cancellation);
                }

                break;
            case P.WorkItem wi:
                {
                    List<Func<Task>> operations = [];

                    // Resolve activity input
                    if (wi.ActivityRequest is { } ar)
                    {
                        string? input = ar.Input;
                        if (this.RequiresResolution(input))
                        {
                            operations.Add(async () => ar.Input = await this.MaybeResolveAsync(input, cancellation));
                        }
                    }

                    // Resolve orchestration input embedded in ExecutionStarted event and external events
                    if (wi.OrchestratorRequest is { } or)
                    {
                        foreach (P.HistoryEvent e in or.PastEvents)
                        {
                            if (this.RequiresEventPayloadResolution(e))
                            {
                                operations.Add(() => this.ResolveEventPayloadsAsync(e, cancellation));
                            }
                        }

                        foreach (P.HistoryEvent e in or.NewEvents)
                        {
                            if (this.RequiresEventPayloadResolution(e))
                            {
                                operations.Add(() => this.ResolveEventPayloadsAsync(e, cancellation));
                            }
                        }
                    }

                    // Resolve entity V1 batch request (OperationRequest inputs and entity state)
                    if (wi.EntityRequest is { } er1)
                    {
                        string? entityState = er1.EntityState;
                        if (this.RequiresResolution(entityState))
                        {
                            operations.Add(async () => er1.EntityState = await this.MaybeResolveAsync(entityState, cancellation));
                        }

                        if (er1.Operations != null)
                        {
                            foreach (P.OperationRequest op in er1.Operations)
                            {
                                string? input = op.Input;
                                if (this.RequiresResolution(input))
                                {
                                    operations.Add(async () => op.Input = await this.MaybeResolveAsync(input, cancellation));
                                }
                            }
                        }
                    }

                    // Resolve entity V2 request (history-based operation requests and entity state)
                    if (wi.EntityRequestV2 is { } er2)
                    {
                        string? entityState = er2.EntityState;
                        if (this.RequiresResolution(entityState))
                        {
                            operations.Add(async () => er2.EntityState = await this.MaybeResolveAsync(entityState, cancellation));
                        }

                        if (er2.OperationRequests != null)
                        {
                            foreach (P.HistoryEvent opEvt in er2.OperationRequests)
                            {
                                if (this.RequiresEventPayloadResolution(opEvt))
                                {
                                    operations.Add(() => this.ResolveEventPayloadsAsync(opEvt, cancellation));
                                }
                            }
                        }
                    }

                    await RunWithBoundedConcurrencyAsync(operations, cancellation);
                }

                break;
        }
    }

    void AddOrchestrationStateResolutionOperations(
        List<Func<Task>> operations,
        P.OrchestrationState state,
        CancellationToken cancellation)
    {
        // Snapshot before dispatch so a queued operation never reads a field after another
        // operation has begun mutating this shared message. Locks cover assignment only: current
        // generated setters are simple reference writes, but protobuf's message API does not
        // guarantee concurrent mutation or that future generated setters remain independent.
        string? input = state.Input;
        if (this.RequiresResolution(input))
        {
            operations.Add(async () =>
            {
                string? resolvedInput = await this.MaybeResolveAsync(input, cancellation);
                this.BeforeSharedMessageLockForTest?.Invoke();
                lock (state)
                {
                    this.SharedMessageLockAcquiredForTest?.Invoke(state);
                    state.Input = resolvedInput;
                }
            });
        }

        string? output = state.Output;
        if (this.RequiresResolution(output))
        {
            operations.Add(async () =>
            {
                string? resolvedOutput = await this.MaybeResolveAsync(output, cancellation);
                this.BeforeSharedMessageLockForTest?.Invoke();
                lock (state)
                {
                    this.SharedMessageLockAcquiredForTest?.Invoke(state);
                    state.Output = resolvedOutput;
                }
            });
        }

        string? customStatus = state.CustomStatus;
        if (this.RequiresResolution(customStatus))
        {
            operations.Add(async () =>
            {
                string? resolvedCustomStatus = await this.MaybeResolveAsync(customStatus, cancellation);
                this.BeforeSharedMessageLockForTest?.Invoke();
                lock (state)
                {
                    this.SharedMessageLockAcquiredForTest?.Invoke(state);
                    state.CustomStatus = resolvedCustomStatus;
                }
            });
        }
    }

    async Task ExternalizeOrchestratorResponseAsync(P.OrchestratorResponse r, CancellationToken cancellation)
    {
        List<Func<Task>> operations = [];
        string? customStatus = r.CustomStatus;
        if (this.TryGetExternalizationSize(customStatus, out int customStatusSize))
        {
            operations.Add(
                async () => r.CustomStatus =
                    await this.ExternalizePayloadAsync(customStatus!, customStatusSize, cancellation));
        }

        foreach (P.OrchestratorAction a in r.Actions)
        {
            if (a.CompleteOrchestration is { } complete)
            {
                string? result = complete.Result;
                if (this.TryGetExternalizationSize(result, out int resultSize))
                {
                    operations.Add(async () =>
                    {
                        string externalizedResult =
                            await this.ExternalizePayloadAsync(result!, resultSize, cancellation);
                        this.BeforeSharedMessageLockForTest?.Invoke();
                        lock (complete)
                        {
                            this.SharedMessageLockAcquiredForTest?.Invoke(complete);
                            complete.Result = externalizedResult;
                        }
                    });
                }

                string? details = complete.Details;
                if (this.TryGetExternalizationSize(details, out int detailsSize))
                {
                    operations.Add(async () =>
                    {
                        string externalizedDetails =
                            await this.ExternalizePayloadAsync(details!, detailsSize, cancellation);
                        this.BeforeSharedMessageLockForTest?.Invoke();
                        lock (complete)
                        {
                            this.SharedMessageLockAcquiredForTest?.Invoke(complete);
                            complete.Details = externalizedDetails;
                        }
                    });
                }
            }

            if (a.TerminateOrchestration is { } term)
            {
                string? reason = term.Reason;
                if (this.TryGetExternalizationSize(reason, out int reasonSize))
                {
                    operations.Add(
                        async () => term.Reason =
                            await this.ExternalizePayloadAsync(reason!, reasonSize, cancellation));
                }
            }

            if (a.ScheduleTask is { } schedule)
            {
                string? input = schedule.Input;
                if (this.TryGetExternalizationSize(input, out int inputSize))
                {
                    operations.Add(
                        async () => schedule.Input =
                            await this.ExternalizePayloadAsync(input!, inputSize, cancellation));
                }
            }

            if (a.CreateSubOrchestration is { } sub)
            {
                string? input = sub.Input;
                if (this.TryGetExternalizationSize(input, out int inputSize))
                {
                    operations.Add(
                        async () => sub.Input =
                            await this.ExternalizePayloadAsync(input!, inputSize, cancellation));
                }
            }

            if (a.SendEvent is { } sendEvt)
            {
                string? data = sendEvt.Data;
                if (this.TryGetExternalizationSize(data, out int dataSize))
                {
                    operations.Add(
                        async () => sendEvt.Data =
                            await this.ExternalizePayloadAsync(data!, dataSize, cancellation));
                }
            }

            if (a.SendEntityMessage is { } entityMsg)
            {
                if (entityMsg.EntityOperationSignaled is { } sig)
                {
                    string? input = sig.Input;
                    if (this.TryGetExternalizationSize(input, out int inputSize))
                    {
                        operations.Add(
                            async () => sig.Input =
                                await this.ExternalizePayloadAsync(input!, inputSize, cancellation));
                    }
                }

                if (entityMsg.EntityOperationCalled is { } called)
                {
                    string? input = called.Input;
                    if (this.TryGetExternalizationSize(input, out int inputSize))
                    {
                        operations.Add(
                            async () => called.Input =
                                await this.ExternalizePayloadAsync(input!, inputSize, cancellation));
                    }
                }
            }
        }

        await RunWithBoundedConcurrencyAsync(operations, cancellation);
    }

    async Task ExternalizeEntityBatchResultAsync(P.EntityBatchResult r, CancellationToken cancellation)
    {
        List<Func<Task>> operations = [];
        string? entityState = r.EntityState;
        if (this.TryGetExternalizationSize(entityState, out int entityStateSize))
        {
            operations.Add(
                async () => r.EntityState =
                    await this.ExternalizePayloadAsync(entityState!, entityStateSize, cancellation));
        }

        if (r.Results != null)
        {
            foreach (P.OperationResult result in r.Results)
            {
                if (result.Success is { } success)
                {
                    string? resultValue = success.Result;
                    if (this.TryGetExternalizationSize(resultValue, out int resultSize))
                    {
                        operations.Add(
                            async () => success.Result =
                                await this.ExternalizePayloadAsync(resultValue!, resultSize, cancellation));
                    }
                }
            }
        }

        if (r.Actions != null)
        {
            foreach (P.OperationAction action in r.Actions)
            {
                if (action.SendSignal is { } sendSig)
                {
                    string? input = sendSig.Input;
                    if (this.TryGetExternalizationSize(input, out int inputSize))
                    {
                        operations.Add(
                            async () => sendSig.Input =
                                await this.ExternalizePayloadAsync(input!, inputSize, cancellation));
                    }
                }

                if (action.StartNewOrchestration is { } start)
                {
                    string? input = start.Input;
                    if (this.TryGetExternalizationSize(input, out int inputSize))
                    {
                        operations.Add(
                            async () => start.Input =
                                await this.ExternalizePayloadAsync(input!, inputSize, cancellation));
                    }
                }
            }
        }

        await RunWithBoundedConcurrencyAsync(operations, cancellation);
    }

    async Task ExternalizeEntityBatchRequestAsync(P.EntityBatchRequest r, CancellationToken cancellation)
    {
        List<Func<Task>> operations = [];
        string? entityState = r.EntityState;
        if (this.TryGetExternalizationSize(entityState, out int entityStateSize))
        {
            operations.Add(
                async () => r.EntityState =
                    await this.ExternalizePayloadAsync(entityState!, entityStateSize, cancellation));
        }

        if (r.Operations != null)
        {
            foreach (P.OperationRequest op in r.Operations)
            {
                string? input = op.Input;
                if (this.TryGetExternalizationSize(input, out int inputSize))
                {
                    operations.Add(
                        async () => op.Input =
                            await this.ExternalizePayloadAsync(input!, inputSize, cancellation));
                }
            }
        }

        await RunWithBoundedConcurrencyAsync(operations, cancellation);
    }

    bool RequiresEventPayloadResolution(P.HistoryEvent e)
    {
        return e.EventTypeCase switch
        {
            P.HistoryEvent.EventTypeOneofCase.ExecutionStarted =>
                this.RequiresResolution(e.ExecutionStarted?.Input),
            P.HistoryEvent.EventTypeOneofCase.ExecutionCompleted =>
                this.RequiresResolution(e.ExecutionCompleted?.Result),
            P.HistoryEvent.EventTypeOneofCase.EventRaised =>
                this.RequiresResolution(e.EventRaised?.Input),
            P.HistoryEvent.EventTypeOneofCase.TaskScheduled =>
                this.RequiresResolution(e.TaskScheduled?.Input),
            P.HistoryEvent.EventTypeOneofCase.TaskCompleted =>
                this.RequiresResolution(e.TaskCompleted?.Result),
            P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCreated =>
                this.RequiresResolution(e.SubOrchestrationInstanceCreated?.Input),
            P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCompleted =>
                this.RequiresResolution(e.SubOrchestrationInstanceCompleted?.Result),
            P.HistoryEvent.EventTypeOneofCase.EventSent =>
                this.RequiresResolution(e.EventSent?.Input),
            P.HistoryEvent.EventTypeOneofCase.GenericEvent =>
                this.RequiresResolution(e.GenericEvent?.Data),
            P.HistoryEvent.EventTypeOneofCase.ContinueAsNew =>
                this.RequiresResolution(e.ContinueAsNew?.Input),
            P.HistoryEvent.EventTypeOneofCase.ExecutionTerminated =>
                this.RequiresResolution(e.ExecutionTerminated?.Input),
            P.HistoryEvent.EventTypeOneofCase.ExecutionSuspended =>
                this.RequiresResolution(e.ExecutionSuspended?.Input),
            P.HistoryEvent.EventTypeOneofCase.ExecutionResumed =>
                this.RequiresResolution(e.ExecutionResumed?.Input),
            P.HistoryEvent.EventTypeOneofCase.EntityOperationSignaled =>
                this.RequiresResolution(e.EntityOperationSignaled?.Input),
            P.HistoryEvent.EventTypeOneofCase.EntityOperationCalled =>
                this.RequiresResolution(e.EntityOperationCalled?.Input),
            P.HistoryEvent.EventTypeOneofCase.EntityOperationCompleted =>
                this.RequiresResolution(e.EntityOperationCompleted?.Output),
            P.HistoryEvent.EventTypeOneofCase.HistoryState =>
                e.HistoryState?.OrchestrationState is { } state
                && (this.RequiresResolution(state.Input)
                    || this.RequiresResolution(state.Output)
                    || this.RequiresResolution(state.CustomStatus)),
            _ => false,
        };
    }

    async Task ResolveEventPayloadsAsync(P.HistoryEvent e, CancellationToken cancellation)
    {
        switch (e.EventTypeCase)
        {
            case P.HistoryEvent.EventTypeOneofCase.ExecutionStarted:
                if (e.ExecutionStarted is { } es)
                {
                    es.Input = await this.MaybeResolveAsync(es.Input, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.ExecutionCompleted:
                if (e.ExecutionCompleted is { } ec)
                {
                    ec.Result = await this.MaybeResolveAsync(ec.Result, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.EventRaised:
                if (e.EventRaised is { } er)
                {
                    er.Input = await this.MaybeResolveAsync(er.Input, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.TaskScheduled:
                if (e.TaskScheduled is { } ts)
                {
                    ts.Input = await this.MaybeResolveAsync(ts.Input, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.TaskCompleted:
                if (e.TaskCompleted is { } tc)
                {
                    tc.Result = await this.MaybeResolveAsync(tc.Result, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCreated:
                if (e.SubOrchestrationInstanceCreated is { } soc)
                {
                    soc.Input = await this.MaybeResolveAsync(soc.Input, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCompleted:
                if (e.SubOrchestrationInstanceCompleted is { } sox)
                {
                    sox.Result = await this.MaybeResolveAsync(sox.Result, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.EventSent:
                if (e.EventSent is { } esent)
                {
                    esent.Input = await this.MaybeResolveAsync(esent.Input, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.GenericEvent:
                if (e.GenericEvent is { } ge)
                {
                    ge.Data = await this.MaybeResolveAsync(ge.Data, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.ContinueAsNew:
                if (e.ContinueAsNew is { } can)
                {
                    can.Input = await this.MaybeResolveAsync(can.Input, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.ExecutionTerminated:
                if (e.ExecutionTerminated is { } et)
                {
                    et.Input = await this.MaybeResolveAsync(et.Input, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.ExecutionSuspended:
                if (e.ExecutionSuspended is { } esus)
                {
                    esus.Input = await this.MaybeResolveAsync(esus.Input, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.ExecutionResumed:
                if (e.ExecutionResumed is { } eres)
                {
                    eres.Input = await this.MaybeResolveAsync(eres.Input, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.EntityOperationSignaled:
                if (e.EntityOperationSignaled is { } eos)
                {
                    eos.Input = await this.MaybeResolveAsync(eos.Input, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.EntityOperationCalled:
                if (e.EntityOperationCalled is { } eoc)
                {
                    eoc.Input = await this.MaybeResolveAsync(eoc.Input, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.EntityOperationCompleted:
                if (e.EntityOperationCompleted is { } ecomp)
                {
                    ecomp.Output = await this.MaybeResolveAsync(ecomp.Output, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.HistoryState:
                if (e.HistoryState is { } hs && hs.OrchestrationState is { } os)
                {
                    // A HistoryState event is one bounded operation, so its three fields remain
                    // sequential. Concurrent history operations always target different messages.
                    os.Input = await this.MaybeResolveAsync(os.Input, cancellation);
                    os.Output = await this.MaybeResolveAsync(os.Output, cancellation);
                    os.CustomStatus = await this.MaybeResolveAsync(os.CustomStatus, cancellation);
                }

                break;
        }
    }

    /// <summary>
    /// Runs a set of independent Azure Blob payload externalization/resolution operations with
    /// bounded concurrency, instead of either awaiting them one at a time (additive latency) or
    /// firing them all at once via an unbounded <see cref="Task.WhenAll(IEnumerable{Task})"/>
    /// (creating a per-message burst for messages with many payloads). Each delegate is
    /// expected to assign its result to a distinct protobuf field/element, so the relative
    /// completion order between operations does not affect correctness. Blob I/O is concurrent,
    /// but assignments targeting fields on the same protobuf message are serialized rather than
    /// relying on current generated setter implementation details.
    /// </summary>
    /// <remarks>
    /// Cancellation is honored before starting any operation not already in flight. If any
    /// operation throws (e.g. <see cref="PayloadStorageException"/> for an oversized payload, or
    /// a non-retriable <see cref="RequestFailedException"/>), the lowest-ordinal failed operation
    /// propagates after all started operations have drained. This matches a sequential await
    /// chain's message-order failure semantics and takes precedence over a concurrent cancellation,
    /// preserving the existing caller behavior (e.g. converting permanent failures into a
    /// <see cref="P.TaskFailureDetails"/> completion).
    /// Messages with no eligible Blob operation return without observing cancellation, preserving
    /// the prior no-op behavior; cancellation is surfaced when it prevents actual payload I/O.
    /// <para>
    /// Every started operation is always drained (fully awaited) before this method returns or
    /// throws -- including when stopping early due to cancellation or a prior failure -- so that
    /// (a) the bounding <see cref="SemaphoreSlim"/> is only disposed once nothing can call
    /// <see cref="SemaphoreSlim.Release()"/> on it, and (b) no operation can mutate its target
    /// protobuf field after control has already passed back to the caller.
    /// </para>
    /// </remarks>
    /// <param name="operations">The independent Blob I/O operations to run.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <param name="afterAdvisoryDispatchCheckForTest">Optional test callback after advisory eligibility but before dispatch claim.</param>
    /// <param name="failureRecordedForTest">Optional test callback after an operation's failure is recorded.</param>
    static async Task RunWithBoundedConcurrencyAsync(
        List<Func<Task>> operations,
        CancellationToken cancellation,
        Action<int>? afterAdvisoryDispatchCheckForTest = null,
        Action<int>? failureRecordedForTest = null)
    {
        if (operations.Count == 0)
        {
            // No storage operation exists to cancel.
            return;
        }

        if (operations.Count == 1)
        {
            // Fast path: avoid semaphore/list overhead for the overwhelmingly common single-field case.
            cancellation.ThrowIfCancellationRequested();
            await operations[0]();
            return;
        }

        SemaphoreSlim throttle = new(MaxConcurrentPayloadOperations, MaxConcurrentPayloadOperations);
        List<Task> inFlight = new(operations.Count);
        object failureLock = new();
        Exception? lowestOrdinalFailure = null;
        int lowestFailureOrdinal = int.MaxValue;
        int failureRecorded = 0;
        bool cancellationPreventedDispatch = false;

        for (int operationOrdinal = 0; operationOrdinal < operations.Count; operationOrdinal++)
        {
            Func<Task> operation = operations[operationOrdinal];

            // Stop dispatching new Azure Storage requests once an earlier operation has failed or
            // cancellation has been requested. Operations already started are *not* abandoned here
            // -- they are drained below -- since they may already have side effects (e.g. an
            // in-flight upload) and must not touch the semaphore, or mutate their target field,
            // after this method has returned control to the caller.
            if (AdvisoryShouldStopDispatch())
            {
                break;
            }

            // Deliberately passing CancellationToken.None (not `cancellation`) to WaitAsync: every
            // dispatched operation is itself cancellation-aware (it receives the same token), so a
            // slot reliably frees up as soon as an in-flight operation observes cancellation --
            // without risking a WaitAsync-triggered unwind that would abandon an already-tracked
            // operation.
            await throttle.WaitAsync(CancellationToken.None);

            // Avoid taking the claim lock when a failure or cancellation is already visible. This
            // check is advisory: the authoritative decision remains the locked claim below.
            if (AdvisoryShouldStopDispatch())
            {
                throttle.Release();
                break;
            }

            // A test can pause after advisory eligibility passed to deterministically exercise a
            // failure racing with the authoritative dispatch claim.
            try
            {
                afterAdvisoryDispatchCheckForTest?.Invoke(operationOrdinal);
            }
            catch (Exception ex)
            {
                // The callback runs after acquiring a slot but before claiming the operation. Route
                // test failures through the normal drain path and release the unclaimed slot.
                RecordFailure(ex, operationOrdinal);
                throttle.Release();
                break;
            }

            // Atomically claim this operation under the same lock used to record failures. The
            // successful claim is the operation's dispatch linearization point: a failure recorded
            // afterward does not revoke it, but no operation can claim dispatch after a failure is
            // recorded. Invoke the delegate outside the lock so arbitrary operation code never
            // runs while holding shared helper state.
            bool dispatchClaimed;
            lock (failureLock)
            {
                if (lowestOrdinalFailure != null)
                {
                    dispatchClaimed = false;
                }
                else if (cancellation.IsCancellationRequested)
                {
                    cancellationPreventedDispatch = true;
                    dispatchClaimed = false;
                }
                else
                {
                    dispatchClaimed = true;
                }
            }

            if (!dispatchClaimed)
            {
                throttle.Release();
                break;
            }

            inFlight.Add(TrackAsync(operation, operationOrdinal));
        }

        try
        {
            // Wait for every started operation to finish -- regardless of outcome -- before this
            // method returns or throws. TrackAsync captures operation failures in
            // `lowestOrdinalFailure`. Even if an optional test callback faults a TrackAsync task,
            // Task.WhenAll waits for every task to complete before propagating that test failure.
            await Task.WhenAll(inFlight);
        }
        finally
        {
            // Safe only because every TrackAsync call above (including its own `finally`, which
            // releases a semaphore slot) has already run to completion by the time
            // Task.WhenAll(inFlight) returns.
            throttle.Dispose();
        }

        Exception? failureToThrow;
        bool throwCancellation;
        lock (failureLock)
        {
            failureToThrow = lowestOrdinalFailure;
            throwCancellation = cancellationPreventedDispatch;
        }

        if (failureToThrow != null)
        {
            // Rethrow the first failure in message order with its original stack trace preserved,
            // matching the prior sequential await-per-field behavior (and never masked by e.g.
            // a later cancellation or a disposed-resource exception).
            ExceptionDispatchInfo.Capture(failureToThrow).Throw();
        }

        if (throwCancellation)
        {
            // Synthesize cancellation only when it actually prevented a pending operation from
            // claiming dispatch. Cancellation observed after every operation was already claimed
            // must not convert otherwise successful completed work into cancellation.
            cancellation.ThrowIfCancellationRequested();
        }

        bool AdvisoryShouldStopDispatch()
        {
            if (Volatile.Read(ref failureRecorded) != 0)
            {
                return true;
            }

            if (cancellation.IsCancellationRequested)
            {
                cancellationPreventedDispatch = true;
                return true;
            }

            return false;
        }

        void RecordFailure(Exception ex, int operationOrdinal)
        {
            lock (failureLock)
            {
                if (operationOrdinal < lowestFailureOrdinal)
                {
                    lowestFailureOrdinal = operationOrdinal;
                    lowestOrdinalFailure = ex;
                }

                Volatile.Write(ref failureRecorded, 1);
            }
        }

        async Task TrackAsync(Func<Task> operation, int operationOrdinal)
        {
            try
            {
                await operation();
            }
            catch (Exception ex)
            {
                // Preserve the lowest-ordinal failure, rather than whichever concurrently running
                // operation happened to finish first. The exception is captured -- not rethrown --
                // so operation failures do not fault Task.WhenAll. The optional test callback below
                // can still fault this task, but Task.WhenAll drains all tasks before propagating it.
                RecordFailure(ex, operationOrdinal);
                failureRecordedForTest?.Invoke(operationOrdinal);
            }
            finally
            {
                throttle.Release();
            }
        }
    }

    /// <summary>
    /// Determines whether an exception represents a permanent storage failure that will never
    /// succeed on retry, such as payload exceeding the configured maximum or 4xx HTTP errors
    /// (authentication, authorization, not found).
    /// </summary>
    static bool IsPermanentStorageFailure(Exception ex)
    {
        if (ex is PayloadStorageException)
        {
            return true;
        }

        // Azure SDK retries 408 (Request Timeout) and 429 (Too Many Requests) automatically
        // (see ResponseClassifier.IsRetriableResponse in Azure.Core). All other 4xx status codes
        // are NOT retried, meaning the request is fundamentally invalid
        // (e.g., 401 bad credentials, 403 missing RBAC role, 404 account/container not found).
        // These will never succeed on retry with the same configuration.
        if (ex is RequestFailedException rfe
            && rfe.Status >= 400 && rfe.Status < 500
            && rfe.Status != 408 && rfe.Status != 429)
        {
            return true;
        }

        return false;
    }
}
