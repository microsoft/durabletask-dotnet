// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
                r.Result = await this.MaybeExternalizeAsync(r.Result, cancellation);
                break;
            case P.OrchestratorResponse r:
                await this.ExternalizeOrchestratorResponseAsync(r, cancellation);
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
                s.Input = await this.MaybeResolveAsync(s.Input, cancellation);
                s.Output = await this.MaybeResolveAsync(s.Output, cancellation);
                s.CustomStatus = await this.MaybeResolveAsync(s.CustomStatus, cancellation);
                break;
            case P.HistoryChunk c when c.Events != null:
                foreach (P.HistoryEvent e in c.Events)
                {
                    await this.ResolveEventPayloadsAsync(e, cancellation);
                }

                break;
            case P.QueryInstancesResponse r:
                foreach (P.OrchestrationState s in r.OrchestrationState)
                {
                    s.Input = await this.MaybeResolveAsync(s.Input, cancellation);
                    s.Output = await this.MaybeResolveAsync(s.Output, cancellation);
                    s.CustomStatus = await this.MaybeResolveAsync(s.CustomStatus, cancellation);
                }

                break;
            case P.GetEntityResponse r when r.Entity is { } em:
                em.SerializedState = await this.MaybeResolveAsync(em.SerializedState, cancellation);
                break;
            case P.QueryEntitiesResponse r:
                foreach (P.EntityMetadata em in r.Entities)
                {
                    em.SerializedState = await this.MaybeResolveAsync(em.SerializedState, cancellation);
                }

                break;
            case P.WorkItem wi:
                // Resolve activity input
                if (wi.ActivityRequest is { } ar)
                {
                    ar.Input = await this.MaybeResolveAsync(ar.Input, cancellation);
                }

                // Resolve orchestration input embedded in ExecutionStarted event and external events
                if (wi.OrchestratorRequest is { } or)
                {
                    foreach (P.HistoryEvent? e in or.PastEvents)
                    {
                        await this.ResolveEventPayloadsAsync(e, cancellation);
                    }

                    foreach (P.HistoryEvent? e in or.NewEvents)
                    {
                        await this.ResolveEventPayloadsAsync(e, cancellation);
                    }
                }

                // Resolve entity V1 batch request (OperationRequest inputs and entity state)
                if (wi.EntityRequest is { } er1)
                {
                    er1.EntityState = await this.MaybeResolveAsync(er1.EntityState, cancellation);
                    if (er1.Operations != null)
                    {
                        foreach (P.OperationRequest op in er1.Operations)
                        {
                            op.Input = await this.MaybeResolveAsync(op.Input, cancellation);
                        }
                    }
                }

                // Resolve entity V2 request (history-based operation requests and entity state)
                if (wi.EntityRequestV2 is { } er2)
                {
                    er2.EntityState = await this.MaybeResolveAsync(er2.EntityState, cancellation);
                    if (er2.OperationRequests != null)
                    {
                        foreach (P.HistoryEvent opEvt in er2.OperationRequests)
                        {
                            await this.ResolveEventPayloadsAsync(opEvt, cancellation);
                        }
                    }
                }

                break;
        }
    }

    async Task ExternalizeOrchestratorResponseAsync(P.OrchestratorResponse r, CancellationToken cancellation)
    {
        r.CustomStatus = await this.MaybeExternalizeAsync(r.CustomStatus, cancellation);
        foreach (P.OrchestratorAction a in r.Actions)
        {
            if (a.CompleteOrchestration is { } complete)
            {
                complete.Result = await this.MaybeExternalizeAsync(complete.Result, cancellation);
                complete.Details = await this.MaybeExternalizeAsync(complete.Details, cancellation);
            }

            if (a.TerminateOrchestration is { } term)
            {
                term.Reason = await this.MaybeExternalizeAsync(term.Reason, cancellation);
            }

            if (a.ScheduleTask is { } schedule)
            {
                schedule.Input = await this.MaybeExternalizeAsync(schedule.Input, cancellation);
            }

            if (a.CreateSubOrchestration is { } sub)
            {
                sub.Input = await this.MaybeExternalizeAsync(sub.Input, cancellation);
            }

            if (a.SendEvent is { } sendEvt)
            {
                sendEvt.Data = await this.MaybeExternalizeAsync(sendEvt.Data, cancellation);
            }

            if (a.SendEntityMessage is { } entityMsg)
            {
                if (entityMsg.EntityOperationSignaled is { } sig)
                {
                    sig.Input = await this.MaybeExternalizeAsync(sig.Input, cancellation);
                }

                if (entityMsg.EntityOperationCalled is { } called)
                {
                    called.Input = await this.MaybeExternalizeAsync(called.Input, cancellation);
                }
            }
        }
    }

    async Task ExternalizeEntityBatchResultAsync(P.EntityBatchResult r, CancellationToken cancellation)
    {
        r.EntityState = await this.MaybeExternalizeAsync(r.EntityState, cancellation);
        if (r.Results != null)
        {
            foreach (P.OperationResult result in r.Results)
            {
                if (result.Success is { } success)
                {
                    success.Result = await this.MaybeExternalizeAsync(success.Result, cancellation);
                }
            }
        }

        if (r.Actions != null)
        {
            foreach (P.OperationAction action in r.Actions)
            {
                if (action.SendSignal is { } sendSig)
                {
                    sendSig.Input = await this.MaybeExternalizeAsync(sendSig.Input, cancellation);
                }

                if (action.StartNewOrchestration is { } start)
                {
                    start.Input = await this.MaybeExternalizeAsync(start.Input, cancellation);
                }
            }
        }
    }

    async Task ExternalizeEntityBatchRequestAsync(P.EntityBatchRequest r, CancellationToken cancellation)
    {
        r.EntityState = await this.MaybeExternalizeAsync(r.EntityState, cancellation);
        if (r.Operations != null)
        {
            foreach (P.OperationRequest op in r.Operations)
            {
                op.Input = await this.MaybeExternalizeAsync(op.Input, cancellation);
            }
        }
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
}
