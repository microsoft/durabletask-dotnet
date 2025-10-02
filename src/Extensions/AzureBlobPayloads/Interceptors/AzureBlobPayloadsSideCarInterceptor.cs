// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core.Interceptors;

using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask;

/// <summary>
/// gRPC interceptor that externalizes large payloads to an <see cref="IPayloadStore"/> on requests
/// and resolves known payload tokens on responses for SideCar.
/// </summary>
public sealed class AzureBlobPayloadsSideCarInterceptor(IPayloadStore payloadStore, LargePayloadStorageOptions options)
    : BasePayloadInterceptor<object, object>(payloadStore, options)
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
                await this.MaybeResolveAsync(v => s.Input = v, s.Input, cancellation);
                await this.MaybeResolveAsync(v => s.Output = v, s.Output, cancellation);
                await this.MaybeResolveAsync(v => s.CustomStatus = v, s.CustomStatus, cancellation);
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
                    await this.MaybeResolveAsync(v => s.Input = v, s.Input, cancellation);
                    await this.MaybeResolveAsync(v => s.Output = v, s.Output, cancellation);
                    await this.MaybeResolveAsync(v => s.CustomStatus = v, s.CustomStatus, cancellation);
                }

                break;
            case P.GetEntityResponse r when r.Entity is { } em:
                await this.MaybeResolveAsync(v => em.SerializedState = v, em.SerializedState, cancellation);
                break;
            case P.QueryEntitiesResponse r:
                foreach (P.EntityMetadata em in r.Entities)
                {
                    await this.MaybeResolveAsync(v => em.SerializedState = v, em.SerializedState, cancellation);
                }

                break;
            case P.WorkItem wi:
                // Resolve activity input
                if (wi.ActivityRequest is { } ar)
                {
                    await this.MaybeResolveAsync(v => ar.Input = v, ar.Input, cancellation);
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
                    await this.MaybeResolveAsync(v => er1.EntityState = v, er1.EntityState, cancellation);
                    if (er1.Operations != null)
                    {
                        foreach (P.OperationRequest op in er1.Operations)
                        {
                            await this.MaybeResolveAsync(v => op.Input = v, op.Input, cancellation);
                        }
                    }
                }

                // Resolve entity V2 request (history-based operation requests and entity state)
                if (wi.EntityRequestV2 is { } er2)
                {
                    await this.MaybeResolveAsync(v => er2.EntityState = v, er2.EntityState, cancellation);
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
                    await this.MaybeResolveAsync(v => es.Input = v, es.Input, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.ExecutionCompleted:
                if (e.ExecutionCompleted is { } ec)
                {
                    await this.MaybeResolveAsync(v => ec.Result = v, ec.Result, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.EventRaised:
                if (e.EventRaised is { } er)
                {
                    await this.MaybeResolveAsync(v => er.Input = v, er.Input, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.TaskScheduled:
                if (e.TaskScheduled is { } ts)
                {
                    await this.MaybeResolveAsync(v => ts.Input = v, ts.Input, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.TaskCompleted:
                if (e.TaskCompleted is { } tc)
                {
                    await this.MaybeResolveAsync(v => tc.Result = v, tc.Result, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCreated:
                if (e.SubOrchestrationInstanceCreated is { } soc)
                {
                    await this.MaybeResolveAsync(v => soc.Input = v, soc.Input, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCompleted:
                if (e.SubOrchestrationInstanceCompleted is { } sox)
                {
                    await this.MaybeResolveAsync(v => sox.Result = v, sox.Result, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.EventSent:
                if (e.EventSent is { } esent)
                {
                    await this.MaybeResolveAsync(v => esent.Input = v, esent.Input, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.GenericEvent:
                if (e.GenericEvent is { } ge)
                {
                    await this.MaybeResolveAsync(v => ge.Data = v, ge.Data, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.ContinueAsNew:
                if (e.ContinueAsNew is { } can)
                {
                    await this.MaybeResolveAsync(v => can.Input = v, can.Input, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.ExecutionTerminated:
                if (e.ExecutionTerminated is { } et)
                {
                    await this.MaybeResolveAsync(v => et.Input = v, et.Input, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.ExecutionSuspended:
                if (e.ExecutionSuspended is { } esus)
                {
                    await this.MaybeResolveAsync(v => esus.Input = v, esus.Input, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.ExecutionResumed:
                if (e.ExecutionResumed is { } eres)
                {
                    await this.MaybeResolveAsync(v => eres.Input = v, eres.Input, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.EntityOperationSignaled:
                if (e.EntityOperationSignaled is { } eos)
                {
                    await this.MaybeResolveAsync(v => eos.Input = v, eos.Input, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.EntityOperationCalled:
                if (e.EntityOperationCalled is { } eoc)
                {
                    await this.MaybeResolveAsync(v => eoc.Input = v, eoc.Input, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.EntityOperationCompleted:
                if (e.EntityOperationCompleted is { } ecomp)
                {
                    await this.MaybeResolveAsync(v => ecomp.Output = v, ecomp.Output, cancellation);
                }

                break;
            case P.HistoryEvent.EventTypeOneofCase.HistoryState:
                if (e.HistoryState is { } hs && hs.OrchestrationState is { } os)
                {
                    await this.MaybeResolveAsync(v => os.Input = v, os.Input, cancellation);
                    await this.MaybeResolveAsync(v => os.Output = v, os.Output, cancellation);
                    await this.MaybeResolveAsync(v => os.CustomStatus = v, os.CustomStatus, cancellation);
                }

                break;
        }
    }
}
