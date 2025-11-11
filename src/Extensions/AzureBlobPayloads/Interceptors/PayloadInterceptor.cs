// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Microsoft.DurableTask;

/// <summary>
/// Base class for gRPC interceptors that externalize large payloads to an <see cref="PayloadStore"/> on requests
/// and resolves known payload tokens on responses.
/// </summary>
/// <typeparam name="TRequestNamespace">The namespace for request message types.</typeparam>
/// <typeparam name="TResponseNamespace">The namespace for response message types.</typeparam>
/// <remarks>
/// Initializes a new instance of the <see cref="PayloadInterceptor{TRequestNamespace, TResponseNamespace}"/> class.
/// </remarks>
/// <param name="payloadStore">The payload store.</param>
/// <param name="options">The options.</param>
public abstract class PayloadInterceptor<TRequestNamespace, TResponseNamespace>(
    PayloadStore payloadStore,
    LargePayloadStorageOptions options) : Interceptor
    where TRequestNamespace : class
    where TResponseNamespace : class
{
    readonly PayloadStore payloadStore = payloadStore;
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

    /// <summary>
    /// Asynchronously streams a response from a server.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="request">The request to process.</param>
    /// <param name="context">The context of the call.</param>
    /// <param name="continuation">The continuation of the call.</param>
    /// <returns>A task representing the async operation.</returns>
    /// <remarks>
    /// Server streaming: resolve payloads in streamed responses (e.g., GetWorkItems).
    /// </remarks>
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

    /// <summary>
    /// Externalizes large payloads in request messages.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <param name="request">The request to process.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>A task representing the async operation.</returns>
    protected abstract Task ExternalizeRequestPayloadsAsync<TRequest>(TRequest request, CancellationToken cancellation);

    /// <summary>
    /// Resolves payload tokens in response messages.
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="response">The response to process.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>A task representing the async operation.</returns>
    protected abstract Task ResolveResponsePayloadsAsync<TResponse>(TResponse response, CancellationToken cancellation);

    /// <summary>
    /// Externalizes a payload if it exceeds the threshold.
    /// </summary>
    /// <param name="value">The value to potentially externalize.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>A task that returns the externalized token or the original value.</returns>
    protected async Task<string?> MaybeExternalizeAsync(string? value, CancellationToken cancellation)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        int size = Encoding.UTF8.GetByteCount(value);
        if (size < this.options.ExternalizeThresholdBytes)
        {
            return value;
        }

        // Enforce a hard cap to prevent unbounded payload sizes
        if (size > this.options.MaxExternalizedPayloadBytes)
        {
            throw new InvalidOperationException(
                $"Payload size {size / 1024} kb exceeds the configured maximum of {this.options.MaxExternalizedPayloadBytes / 1024} kb. " +
                "Consider reducing the payload or increase MaxExternalizedPayloadBytes setting.");
        }

        return await this.payloadStore.UploadAsync(value!, cancellation);
    }

    /// <summary>
    /// Resolves a payload token if it's known to the store.
    /// </summary>
    /// <param name="value">The value to potentially resolve.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>The resolved value or the original value if it's not a known payload token.</returns>
    protected async Task<string?> MaybeResolveAsync(string? value, CancellationToken cancellation)
    {
        if (string.IsNullOrEmpty(value) || !this.payloadStore.IsKnownPayloadToken(value ?? string.Empty))
        {
            return value;
        }

        return await this.payloadStore.DownloadAsync(value!, cancellation);
    }

    sealed class TransformingStreamReader<T>(
        IAsyncStreamReader<T> inner,
        Func<T, CancellationToken, ValueTask<T>> transform) : IAsyncStreamReader<T>
    {
        readonly IAsyncStreamReader<T> inner = inner;
        readonly Func<T, CancellationToken, ValueTask<T>> transform = transform;

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
