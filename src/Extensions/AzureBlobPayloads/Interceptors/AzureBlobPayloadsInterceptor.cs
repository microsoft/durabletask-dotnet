// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.DurableTask.Converters;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc.Internal;

/// <summary>
/// gRPC interceptor that externalizes large payloads to an <see cref="IPayloadStore"/> on requests
/// and resolves known payload tokens on responses.
/// </summary>
sealed class AzureBlobPayloadsInterceptor : Interceptor
{
    readonly IPayloadStore payloadStore;
    readonly LargePayloadStorageOptions options;

    public AzureBlobPayloadsInterceptor(IPayloadStore payloadStore, LargePayloadStorageOptions options)
    {
        this.payloadStore = payloadStore;
        this.options = options;
    }

    // Unary: externalize on request, resolve on response
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        // Mutate request payloads before sending
        this.ExternalizeRequestPayloads(request, context);

        AsyncUnaryCall<TResponse> call = continuation(request, context);

        // Wrap response task to resolve payloads
        async Task<TResponse> ResolveAsync(Task<TResponse> inner)
        {
            TResponse response = await inner.ConfigureAwait(false);
            await this.ResolveResponsePayloadsAsync(response, context.CancellationToken);
            return response;
        }

        return new AsyncUnaryCall<TResponse>(
            ResolveAsync(call.ResponseAsync),
            call.ResponseHeadersAsync,
            call.GetStatus,
            call.GetTrailers,
            call.Dispose);
    }

    // Server streaming: resolve payloads in streamed responses (e.g., GetWorkItems)
    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        this.ExternalizeRequestPayloads(request, context);

        AsyncServerStreamingCall<TResponse> call = continuation(request, context);

        IAsyncStreamReader<TResponse> wrapped = new TransformingStreamReader<TResponse>(call.ResponseStream, async (msg, ct) =>
        {
            await this.ResolveResponsePayloadsAsync(msg, ct).ConfigureAwait(false);
            return msg;
        });

        return new AsyncServerStreamingCall<TResponse>(
            wrapped,
            call.ResponseHeadersAsync,
            call.GetStatus,
            call.GetTrailers,
            call.Dispose);
    }

    void ExternalizeRequestPayloads<TRequest>(TRequest request, ClientInterceptorContext<TRequest, object> context)
    {
        // Client -> sidecar
        switch (request)
        {
            case P.CreateInstanceRequest r:
                this.MaybeExternalize(ref r.Input);
                break;
            case P.RaiseEventRequest r:
                this.MaybeExternalize(ref r.Input);
                break;
            case P.TerminateRequest r:
                this.MaybeExternalize(ref r.Output);
                break;
            case P.ActivityResponse r:
                this.MaybeExternalize(ref r.Result);
                break;
            case P.OrchestratorResponse r:
                this.MaybeExternalize(ref r.CustomStatus);
                foreach (P.OrchestratorAction a in r.Actions)
                {
                    if (a.CompleteOrchestration is { } complete)
                    {
                        this.MaybeExternalize(ref complete.Result);
                    }
                }
                break;
        }
    }

    async Task ResolveResponsePayloadsAsync<TResponse>(TResponse response, CancellationToken cancellation)
    {
        // Sidecar -> client/worker
        switch (response)
        {
            case P.GetInstanceResponse r when r.OrchestrationState is { } s:
                this.MaybeResolve(ref s.Input, cancellation);
                this.MaybeResolve(ref s.Output, cancellation);
                this.MaybeResolve(ref s.CustomStatus, cancellation);
                break;
            case P.QueryInstancesResponse r:
                foreach (P.OrchestrationState s in r.OrchestrationState)
                {
                    this.MaybeResolve(ref s.Input, cancellation);
                    this.MaybeResolve(ref s.Output, cancellation);
                    this.MaybeResolve(ref s.CustomStatus, cancellation);
                }
                break;
            case P.WorkItem wi:
                // Resolve activity input
                if (wi.ActivityRequest is { } ar)
                {
                    this.MaybeResolve(ref ar.Input, cancellation);
                }

                // Resolve orchestration input embedded in ExecutionStarted event and external events
                if (wi.OrchestratorRequest is { } or)
                {
                    foreach (var e in or.PastEvents)
                    {
                        this.ResolveEventPayloads(e, cancellation);
                    }
                    foreach (var e in or.NewEvents)
                    {
                        this.ResolveEventPayloads(e, cancellation);
                    }
                }
                break;
        }
        await Task.CompletedTask;
    }

    void ResolveEventPayloads(P.HistoryEvent e, CancellationToken cancellation)
    {
        switch (e.EventTypeCase)
        {
            case P.HistoryEvent.EventTypeOneofCase.ExecutionStarted:
                if (e.ExecutionStarted is { } es)
                {
                    this.MaybeResolve(ref es.Input, cancellation);
                }
                break;
            case P.HistoryEvent.EventTypeOneofCase.EventRaised:
                if (e.EventRaised is { } er)
                {
                    this.MaybeResolve(ref er.Input, cancellation);
                }
                break;
        }
    }

    void MaybeExternalize(ref string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        int size = Encoding.UTF8.GetByteCount(value);
        if (size < this.options.ExternalizeThresholdBytes)
        {
            return;
        }

        // Upload synchronously via .GetAwaiter().GetResult() because interceptor API is sync for requests
        string token = this.payloadStore.UploadAsync(Encoding.UTF8.GetBytes(value), CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        value = token;
    }

    void MaybeResolve(ref string? value, CancellationToken cancellation)
    {
        if (string.IsNullOrEmpty(value) || !this.payloadStore.IsKnownPayloadToken(value))
        {
            return;
        }

        string resolved = this.payloadStore.DownloadAsync(value, cancellation)
            .GetAwaiter()
            .GetResult();
        value = resolved;
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
            bool hasNext = await this.inner.MoveNext(cancellationToken).ConfigureAwait(false);
            if (!hasNext)
            {
                return false;
            }

            this.Current = await this.transform(this.inner.Current, cancellationToken).ConfigureAwait(false);
            return true;
        }
    }
}


