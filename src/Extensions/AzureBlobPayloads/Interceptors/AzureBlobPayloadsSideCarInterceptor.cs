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
    // Conservative cap on simultaneous Azure Blob uploads/downloads for a single message (e.g. a
    // fan-out orchestrator response, a history chunk, or a batch of entity operations). Bounding
    // concurrency lets independent payload operations overlap -- avoiding additive per-field
    // latency -- without issuing an unbounded burst of requests that could trip Azure Storage
    // account-level throttling (see https://aka.ms/azure-storage-scalability-targets).
    const int MaxConcurrentPayloadOperations = 8;

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
                await RunWithBoundedConcurrencyAsync(
                    [
                        async () =>
                        {
                            string? input = await this.MaybeResolveAsync(s.Input, cancellation);
                            lock (s)
                            {
                                s.Input = input;
                            }
                        },
                        async () =>
                        {
                            string? output = await this.MaybeResolveAsync(s.Output, cancellation);
                            lock (s)
                            {
                                s.Output = output;
                            }
                        },
                        async () =>
                        {
                            string? customStatus = await this.MaybeResolveAsync(s.CustomStatus, cancellation);
                            lock (s)
                            {
                                s.CustomStatus = customStatus;
                            }
                        },
                    ],
                    cancellation);
                break;
            case P.HistoryChunk c when c.Events != null:
                {
                    List<Func<Task>> operations = [];
                    foreach (P.HistoryEvent e in c.Events)
                    {
                        operations.Add(() => this.ResolveEventPayloadsAsync(e, cancellation));
                    }

                    await RunWithBoundedConcurrencyAsync(operations, cancellation);
                }

                break;
            case P.QueryInstancesResponse r:
                {
                    List<Func<Task>> operations = [];
                    foreach (P.OrchestrationState s in r.OrchestrationState)
                    {
                        operations.Add(async () =>
                        {
                            string? input = await this.MaybeResolveAsync(s.Input, cancellation);
                            lock (s)
                            {
                                s.Input = input;
                            }
                        });
                        operations.Add(async () =>
                        {
                            string? output = await this.MaybeResolveAsync(s.Output, cancellation);
                            lock (s)
                            {
                                s.Output = output;
                            }
                        });
                        operations.Add(async () =>
                        {
                            string? customStatus = await this.MaybeResolveAsync(s.CustomStatus, cancellation);
                            lock (s)
                            {
                                s.CustomStatus = customStatus;
                            }
                        });
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
                        operations.Add(async () => em.SerializedState = await this.MaybeResolveAsync(em.SerializedState, cancellation));
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
                        operations.Add(async () => ar.Input = await this.MaybeResolveAsync(ar.Input, cancellation));
                    }

                    // Resolve orchestration input embedded in ExecutionStarted event and external events
                    if (wi.OrchestratorRequest is { } or)
                    {
                        foreach (P.HistoryEvent? e in or.PastEvents)
                        {
                            operations.Add(() => this.ResolveEventPayloadsAsync(e, cancellation));
                        }

                        foreach (P.HistoryEvent? e in or.NewEvents)
                        {
                            operations.Add(() => this.ResolveEventPayloadsAsync(e, cancellation));
                        }
                    }

                    // Resolve entity V1 batch request (OperationRequest inputs and entity state)
                    if (wi.EntityRequest is { } er1)
                    {
                        operations.Add(async () => er1.EntityState = await this.MaybeResolveAsync(er1.EntityState, cancellation));
                        if (er1.Operations != null)
                        {
                            foreach (P.OperationRequest op in er1.Operations)
                            {
                                operations.Add(async () => op.Input = await this.MaybeResolveAsync(op.Input, cancellation));
                            }
                        }
                    }

                    // Resolve entity V2 request (history-based operation requests and entity state)
                    if (wi.EntityRequestV2 is { } er2)
                    {
                        operations.Add(async () => er2.EntityState = await this.MaybeResolveAsync(er2.EntityState, cancellation));
                        if (er2.OperationRequests != null)
                        {
                            foreach (P.HistoryEvent opEvt in er2.OperationRequests)
                            {
                                operations.Add(() => this.ResolveEventPayloadsAsync(opEvt, cancellation));
                            }
                        }
                    }

                    await RunWithBoundedConcurrencyAsync(operations, cancellation);
                }

                break;
        }
    }

    async Task ExternalizeOrchestratorResponseAsync(P.OrchestratorResponse r, CancellationToken cancellation)
    {
        List<Func<Task>> operations = [async () => r.CustomStatus = await this.MaybeExternalizeAsync(r.CustomStatus, cancellation)];

        foreach (P.OrchestratorAction a in r.Actions)
        {
            if (a.CompleteOrchestration is { } complete)
            {
                operations.Add(async () =>
                {
                    string? result = await this.MaybeExternalizeAsync(complete.Result, cancellation);
                    lock (complete)
                    {
                        complete.Result = result;
                    }
                });
                operations.Add(async () =>
                {
                    string? details = await this.MaybeExternalizeAsync(complete.Details, cancellation);
                    lock (complete)
                    {
                        complete.Details = details;
                    }
                });
            }

            if (a.TerminateOrchestration is { } term)
            {
                operations.Add(async () => term.Reason = await this.MaybeExternalizeAsync(term.Reason, cancellation));
            }

            if (a.ScheduleTask is { } schedule)
            {
                operations.Add(async () => schedule.Input = await this.MaybeExternalizeAsync(schedule.Input, cancellation));
            }

            if (a.CreateSubOrchestration is { } sub)
            {
                operations.Add(async () => sub.Input = await this.MaybeExternalizeAsync(sub.Input, cancellation));
            }

            if (a.SendEvent is { } sendEvt)
            {
                operations.Add(async () => sendEvt.Data = await this.MaybeExternalizeAsync(sendEvt.Data, cancellation));
            }

            if (a.SendEntityMessage is { } entityMsg)
            {
                if (entityMsg.EntityOperationSignaled is { } sig)
                {
                    operations.Add(async () => sig.Input = await this.MaybeExternalizeAsync(sig.Input, cancellation));
                }

                if (entityMsg.EntityOperationCalled is { } called)
                {
                    operations.Add(async () => called.Input = await this.MaybeExternalizeAsync(called.Input, cancellation));
                }
            }
        }

        await RunWithBoundedConcurrencyAsync(operations, cancellation);
    }

    async Task ExternalizeEntityBatchResultAsync(P.EntityBatchResult r, CancellationToken cancellation)
    {
        List<Func<Task>> operations = [async () => r.EntityState = await this.MaybeExternalizeAsync(r.EntityState, cancellation)];

        if (r.Results != null)
        {
            foreach (P.OperationResult result in r.Results)
            {
                if (result.Success is { } success)
                {
                    operations.Add(async () => success.Result = await this.MaybeExternalizeAsync(success.Result, cancellation));
                }
            }
        }

        if (r.Actions != null)
        {
            foreach (P.OperationAction action in r.Actions)
            {
                if (action.SendSignal is { } sendSig)
                {
                    operations.Add(async () => sendSig.Input = await this.MaybeExternalizeAsync(sendSig.Input, cancellation));
                }

                if (action.StartNewOrchestration is { } start)
                {
                    operations.Add(async () => start.Input = await this.MaybeExternalizeAsync(start.Input, cancellation));
                }
            }
        }

        await RunWithBoundedConcurrencyAsync(operations, cancellation);
    }

    async Task ExternalizeEntityBatchRequestAsync(P.EntityBatchRequest r, CancellationToken cancellation)
    {
        List<Func<Task>> operations = [async () => r.EntityState = await this.MaybeExternalizeAsync(r.EntityState, cancellation)];

        if (r.Operations != null)
        {
            foreach (P.OperationRequest op in r.Operations)
            {
                operations.Add(async () => op.Input = await this.MaybeExternalizeAsync(op.Input, cancellation));
            }
        }

        await RunWithBoundedConcurrencyAsync(operations, cancellation);
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
    /// (risking Azure Storage throttling for messages with many payloads). Each delegate is
    /// expected to assign its result to a distinct protobuf field/element, so the relative
    /// completion order between operations does not affect correctness. Blob I/O is concurrent,
    /// but assignments targeting fields on the same protobuf message are serialized because
    /// Google.Protobuf messages do not support concurrent mutation.
    /// </summary>
    /// <remarks>
    /// Cancellation is honored before starting any operation not already in flight. If any
    /// operation throws (e.g. <see cref="PayloadStorageException"/> for an oversized payload, or
    /// a non-retriable <see cref="RequestFailedException"/>), the lowest-ordinal failed operation
    /// propagates after all started operations have drained. This matches a sequential await
    /// chain's message-order failure semantics and takes precedence over a concurrent cancellation,
    /// preserving the existing caller behavior (e.g. converting permanent failures into a
    /// <see cref="P.TaskFailureDetails"/> completion).
    /// <para>
    /// Every started operation is always drained (fully awaited) before this method returns or
    /// throws -- including when stopping early due to cancellation or a prior failure -- so that
    /// (a) the bounding <see cref="SemaphoreSlim"/> is only disposed once nothing can call
    /// <see cref="SemaphoreSlim.Release()"/> on it, and (b) no operation can mutate its target
    /// protobuf field after control has already passed back to the caller.
    /// </para>
    /// </remarks>
    /// <param name="operations">The independent operations to run.</param>
    /// <param name="cancellation">Cancellation token.</param>
    static async Task RunWithBoundedConcurrencyAsync(List<Func<Task>> operations, CancellationToken cancellation)
    {
        if (operations.Count == 0)
        {
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

        for (int operationOrdinal = 0; operationOrdinal < operations.Count; operationOrdinal++)
        {
            Func<Task> operation = operations[operationOrdinal];

            // Stop dispatching new Azure Storage requests once an earlier operation has failed or
            // cancellation has been requested. Operations already started are *not* abandoned here
            // -- they are drained below -- since they may already have side effects (e.g. an
            // in-flight upload) and must not touch the semaphore, or mutate their target field,
            // after this method has returned control to the caller.
            if (HasFailure() || cancellation.IsCancellationRequested)
            {
                break;
            }

            // Deliberately passing CancellationToken.None (not `cancellation`) to WaitAsync: every
            // dispatched operation is itself cancellation-aware (it receives the same token), so a
            // slot reliably frees up as soon as an in-flight operation observes cancellation --
            // without risking a WaitAsync-triggered unwind that would abandon an already-tracked
            // operation.
            await throttle.WaitAsync(CancellationToken.None);

            // Re-check immediately after acquiring the slot: cancellation or a failure may have
            // occurred while this operation was queued waiting for a slot to free up. If so,
            // release the slot back (this operation never started, so there is nothing to drain
            // for it) and stop dispatching -- otherwise a newly-freed slot could let a new
            // operation start after cancellation/failure was already observed.
            if (HasFailure() || cancellation.IsCancellationRequested)
            {
                throttle.Release();
                break;
            }

            inFlight.Add(TrackAsync(operation, operationOrdinal));
        }

        try
        {
            // Wait for every started operation to finish -- regardless of outcome -- before this
            // method returns or throws. TrackAsync below never lets an exception fault this
            // Task.WhenAll; it only records it in `firstFailure`, so draining always completes.
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
        lock (failureLock)
        {
            failureToThrow = lowestOrdinalFailure;
        }

        if (failureToThrow != null)
        {
            // Rethrow the first failure in message order with its original stack trace preserved,
            // matching the prior sequential await-per-field behavior (and never masked by e.g.
            // a later cancellation or a disposed-resource exception).
            ExceptionDispatchInfo.Capture(failureToThrow).Throw();
        }

        // No operation failed, but cancellation may still have cut dispatch short before every
        // operation was started; surface that the same way a sequential await chain would have.
        cancellation.ThrowIfCancellationRequested();

        bool HasFailure()
        {
            lock (failureLock)
            {
                return lowestOrdinalFailure != null;
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
                // so Task.WhenAll above never faults, guaranteeing every operation, including
                // this finally block, runs to completion before the semaphore is disposed.
                lock (failureLock)
                {
                    if (operationOrdinal < lowestFailureOrdinal)
                    {
                        lowestFailureOrdinal = operationOrdinal;
                        lowestOrdinalFailure = ex;
                    }
                }
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
