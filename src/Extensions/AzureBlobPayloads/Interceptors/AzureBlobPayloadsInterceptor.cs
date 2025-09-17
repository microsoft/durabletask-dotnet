// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.DurableTask.Converters;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask;

/// <summary>
/// gRPC interceptor that externalizes large payloads to an <see cref="IPayloadStore"/> on requests
/// and resolves known payload tokens on responses.
/// </summary>
sealed class AzureBlobPayloadsInterceptor(IPayloadStore payloadStore, LargePayloadStorageOptions options) : Interceptor
{
    readonly IPayloadStore payloadStore = payloadStore;
    readonly LargePayloadStorageOptions options = options;

    // Unary: externalize on request, resolve on response

    /// <inheritdoc/>
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        // Build the underlying call lazily after async externalization
        Task<AsyncUnaryCall<TResponse>> startCallTask = Task.Run(async () =>
        {
            // Externalize first; if this fails, do not proceed to send the gRPC call
            await this.ExternalizeRequestPayloadsAsync(request, context.Options.CancellationToken);
            // Only if externalization succeeds, proceed with the continuation
            return continuation(request, context);
        });

        async Task<TResponse> ResponseAsync()
        {
            AsyncUnaryCall<TResponse> innerCall = await startCallTask;
            TResponse response = await innerCall.ResponseAsync;
            await this.ResolveResponsePayloadsAsync(response, context.Options.CancellationToken);
            return response;
        }

        async Task<Metadata> ResponseHeadersAsync()
        {
            AsyncUnaryCall<TResponse> innerCall = await startCallTask;
            return await innerCall.ResponseHeadersAsync;
        }

        Status GetStatus()
        {
            if (startCallTask.IsCanceled)
            {
                return new Status(StatusCode.Cancelled, "Call was cancelled.");
            }

            if (startCallTask.IsFaulted)
            {
                return new Status(StatusCode.Internal, startCallTask.Exception?.Message ?? "Unknown error");
            }
            if (startCallTask.Status == TaskStatus.RanToCompletion)
            {
                return startCallTask.Result.GetStatus();
            }

            // Not started yet; unknown
            return new Status(StatusCode.Unknown, string.Empty);
        }

        Metadata GetTrailers()
        {
            return startCallTask.Status == TaskStatus.RanToCompletion ? startCallTask.Result.GetTrailers() : [];
        }

        void Dispose()
        {
            _ = startCallTask.ContinueWith(
                t =>
                {
                    if (t.Status == TaskStatus.RanToCompletion)
                    {
                        t.Result.Dispose();
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return new AsyncUnaryCall<TResponse>(
            ResponseAsync(),
            ResponseHeadersAsync(),
            GetStatus,
            GetTrailers,
            Dispose);
    }

    // Server streaming: resolve payloads in streamed responses (e.g., GetWorkItems)

    /// <inheritdoc/>
    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        // For streaming, request externalization is not needed currently
        AsyncServerStreamingCall<TResponse> call = continuation(request, context);

        IAsyncStreamReader<TResponse> wrapped = new TransformingStreamReader<TResponse>(call.ResponseStream, async (msg, ct) =>
        {
            await this.ResolveResponsePayloadsAsync(msg, ct);
            return msg;
        });

        return new AsyncServerStreamingCall<TResponse>(
            wrapped,
            call.ResponseHeadersAsync,
            call.GetStatus,
            call.GetTrailers,
            call.Dispose);
    }

    Task ExternalizeRequestPayloadsAsync<TRequest>(TRequest request, CancellationToken cancellation)
    {
        // Client -> sidecar
        switch (request)
        {
            case P.CreateInstanceRequest r:
                return this.MaybeExternalizeAsync(v => r.Input = v, r.Input, cancellation);
            case P.RaiseEventRequest r:
                return this.MaybeExternalizeAsync(v => r.Input = v, r.Input, cancellation);
            case P.TerminateRequest r:
                return this.MaybeExternalizeAsync(v => r.Output = v, r.Output, cancellation);
            case P.SignalEntityRequest r:
                return this.MaybeExternalizeAsync(v => r.Input = v, r.Input, cancellation);
            case P.ActivityResponse r:
                return this.MaybeExternalizeAsync(v => r.Result = v, r.Result, cancellation);
            case P.OrchestratorResponse r:
                return this.ExternalizeOrchestratorResponseAsync(r, cancellation);
            case P.EntityBatchResult r:
                return this.ExternalizeEntityBatchResultAsync(r, cancellation);
            case P.EntityBatchRequest r:
                return this.ExternalizeEntityBatchRequestAsync(r, cancellation);
            case P.EntityRequest r:
                return this.MaybeExternalizeAsync(v => r.EntityState = v, r.EntityState, cancellation);
        }

        return Task.CompletedTask;
    }

    async Task ExternalizeOrchestratorResponseAsync(P.OrchestratorResponse r, CancellationToken cancellation)
    {
        await this.MaybeExternalizeAsync(v => r.CustomStatus = v, r.CustomStatus, cancellation);
        foreach (P.OrchestratorAction a in r.Actions)
        {
            if (a.CompleteOrchestration is { } complete)
            {
                await this.MaybeExternalizeAsync(v => complete.Result = v, complete.Result, cancellation);
                await this.MaybeExternalizeAsync(v => complete.Details = v, complete.Details, cancellation);
            }
            if (a.TerminateOrchestration is { } term)
            {
                await this.MaybeExternalizeAsync(v => term.Reason = v, term.Reason, cancellation);
            }
            if (a.ScheduleTask is { } schedule)
            {
                await this.MaybeExternalizeAsync(v => schedule.Input = v, schedule.Input, cancellation);
            }
            if (a.CreateSubOrchestration is { } sub)
            {
                await this.MaybeExternalizeAsync(v => sub.Input = v, sub.Input, cancellation);
            }
            if (a.SendEvent is { } sendEvt)
            {
                await this.MaybeExternalizeAsync(v => sendEvt.Data = v, sendEvt.Data, cancellation);
            }
            if (a.SendEntityMessage is { } entityMsg)
            {
                if (entityMsg.EntityOperationSignaled is { } sig)
                {
                    await this.MaybeExternalizeAsync(v => sig.Input = v, sig.Input, cancellation);
                }
                if (entityMsg.EntityOperationCalled is { } called)
                {
                    await this.MaybeExternalizeAsync(v => called.Input = v, called.Input, cancellation);
                }
            }
        }
    }

    async Task ExternalizeEntityBatchResultAsync(P.EntityBatchResult r, CancellationToken cancellation)
    {
        await this.MaybeExternalizeAsync(v => r.EntityState = v, r.EntityState, cancellation);
        if (r.Results != null)
        {
            foreach (P.OperationResult result in r.Results)
            {
                if (result.Success is { } success)
                {
                    await this.MaybeExternalizeAsync(v => success.Result = v, success.Result, cancellation);
                }
            }
        }
        if (r.Actions != null)
        {
            foreach (P.OperationAction action in r.Actions)
            {
                if (action.SendSignal is { } sendSig)
                {
                    await this.MaybeExternalizeAsync(v => sendSig.Input = v, sendSig.Input, cancellation);
                }
                if (action.StartNewOrchestration is { } start)
                {
                    await this.MaybeExternalizeAsync(v => start.Input = v, start.Input, cancellation);
                }
            }
        }
    }

    async Task ExternalizeEntityBatchRequestAsync(P.EntityBatchRequest r, CancellationToken cancellation)
    {
        await this.MaybeExternalizeAsync(v => r.EntityState = v, r.EntityState, cancellation);
        if (r.Operations != null)
        {
            foreach (P.OperationRequest op in r.Operations)
            {
                await this.MaybeExternalizeAsync(v => op.Input = v, op.Input, cancellation);
            }
        }
    }

    async Task ResolveResponsePayloadsAsync<TResponse>(TResponse response, CancellationToken cancellation)
    {
        // Sidecar -> client/worker
        switch (response)
        {
            case P.GetInstanceResponse r when r.OrchestrationState is { } s:
                await this.MaybeResolveAsync(v => s.Input = v, s.Input, cancellation);
                await this.MaybeResolveAsync(v => s.Output = v, s.Output, cancellation);
                await this.MaybeResolveAsync(v => s.CustomStatus = v, s.CustomStatus, cancellation);
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
        }
    }

    Task MaybeExternalizeAsync(Action<string?> assign, string? value, CancellationToken cancellation)
    {
        if (string.IsNullOrEmpty(value))
        {
            return Task.CompletedTask;
        }

        int size = Encoding.UTF8.GetByteCount(value);
        if (size < this.options.ExternalizeThresholdBytes)
        {
            return Task.CompletedTask;
        }

        return UploadAsync();

        async Task UploadAsync()
        {
            string token = await this.payloadStore.UploadAsync(Encoding.UTF8.GetBytes(value), cancellation);
            assign(token);
        }
    }

    async Task MaybeResolveAsync(Action<string?> assign, string? value, CancellationToken cancellation)
    {
        if (string.IsNullOrEmpty(value) || !this.payloadStore.IsKnownPayloadToken(value))
        {
            return;
        }

        string resolved = await this.payloadStore.DownloadAsync(value, cancellation);
        assign(resolved);
    }

    sealed class TransformingStreamReader<T> : IAsyncStreamReader<T>
    {
        readonly IAsyncStreamReader<T> inner;
        readonly Func<T, CancellationToken, ValueTask<T>> transform;

        public TransformingStreamReader(IAsyncStreamReader<T> inner, Func<T, CancellationToken, ValueTask<T>> transform)
        {
            this.inner = inner;
            this.transform = transform;
        }

        public T Current { get; private set; } = default!;

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            bool hasNext = await this.inner.MoveNext(cancellationToken);
            if (!hasNext)
            {
                return false;
            }

            this.Current = await this.transform(this.inner.Current, cancellationToken);
            return true;
        }
    }
}
