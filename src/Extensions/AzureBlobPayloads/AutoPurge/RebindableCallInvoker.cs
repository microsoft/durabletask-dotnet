// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// A <see cref="CallInvoker"/> that forwards every call to whichever invoker is currently bound, and can be
/// re-bound to a replacement at any time.
/// </summary>
/// <remarks>
/// <para>The purge activities reach the backend over the worker's own transport rather than a second
/// connection. That transport is not stable for the life of the process: the worker recreates its gRPC channel
/// when the existing one is wedged, and the invoker it builds is wrapped with the configured interceptors
/// (authentication among them). A client constructed once against a captured invoker would therefore keep
/// using a disposed channel after a recreate. This indirection is what lets a singleton
/// <c>LargePayloadPurgeClient</c> - already constructed, already injected into an activity - route its next
/// call through the worker's replacement invoker.</para>
/// <para>The worker publishes into this type through the internal call-invoker publisher hook, always with the
/// effective post-interceptor invoker, so callers here never bypass the configured chain.</para>
/// <para>Calls in flight when a rebind happens complete against the invoker they already captured; only
/// subsequent calls observe the replacement. The worker defers disposal of the previous channel for exactly
/// this reason.</para>
/// </remarks>
sealed class RebindableCallInvoker : CallInvoker
{
    // Written by the worker (startup and each successful channel recreate) and read by activity call sites on
    // other threads. Volatile access publishes the reference without a lock; each call reads it exactly once so
    // a rebind cannot tear a single call across two invokers.
    CallInvoker? current;

    /// <summary>
    /// Binds (or re-binds) the invoker that subsequent calls are forwarded to.
    /// </summary>
    /// <param name="invoker">The invoker to forward to.</param>
    public void Rebind(CallInvoker invoker)
    {
        Volatile.Write(ref this.current, Check.NotNull(invoker));
    }

    /// <inheritdoc/>
    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        => this.Current().BlockingUnaryCall(method, host, options, request);

    /// <inheritdoc/>
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        => this.Current().AsyncUnaryCall(method, host, options, request);

    /// <inheritdoc/>
    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        => this.Current().AsyncServerStreamingCall(method, host, options, request);

    /// <inheritdoc/>
    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options)
        => this.Current().AsyncClientStreamingCall(method, host, options);

    /// <inheritdoc/>
    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options)
        => this.Current().AsyncDuplexStreamingCall(method, host, options);

    CallInvoker Current()
    {
        // Fail loudly rather than inventing a transport. Reaching here means an activity ran before the worker
        // published its invoker, which cannot happen through the normal path (work items only arrive after the
        // worker has connected) - so a null here is a wiring defect worth surfacing, not a race to paper over.
        return Volatile.Read(ref this.current) ?? throw new InvalidOperationException(
            "The Durable Task worker has not published a gRPC transport yet, so externalized payloads cannot be " +
            "purged. This indicates the purge activities were invoked outside a running gRPC worker.");
    }
}
