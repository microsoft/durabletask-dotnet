// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Client.Grpc;

/// <summary>
/// A <see cref="CallInvoker"/> wrapper that observes RPC outcomes and triggers a fire-and-forget channel
/// recreation after a configurable number of consecutive transport failures
/// (<see cref="StatusCode.Unavailable"/>, or <see cref="StatusCode.DeadlineExceeded"/> on RPCs that are
/// not long-poll waits). This guards against half-open HTTP/2 connections that can otherwise wedge
/// an entire client process for the lifetime of the gRPC channel.
/// </summary>
/// <remarks>
/// <para>The wrapper holds an immutable <see cref="TransportState"/> (channel + invoker pair) and swaps
/// the entire pair atomically on recreate to avoid torn state. Streaming RPCs are forwarded without
/// outcome observation; only unary RPC outcomes count toward the failure threshold.</para>
/// <para>The triggering RPC still surfaces its original failure to the caller; subsequent RPCs benefit
/// from the recreated channel.</para>
/// </remarks>
sealed class ChannelRecreatingCallInvoker : CallInvoker, IAsyncDisposable
{
    /// <summary>
    /// Methods on which a <see cref="StatusCode.DeadlineExceeded"/> response is expected behavior
    /// (long-poll-style waits) and must NOT be counted toward the recreate threshold.
    /// </summary>
    static readonly HashSet<string> DeadlineExceededAllowedMethods = new(StringComparer.Ordinal)
    {
        "/TaskHubSidecarService/WaitForInstanceCompletion",
        "/TaskHubSidecarService/WaitForInstanceStart",
    };

    readonly Func<GrpcChannel, CancellationToken, Task<GrpcChannel>> recreator;
    readonly int failureThreshold;
    readonly TimeSpan minRecreateInterval;
    readonly bool ownsChannel;
    readonly ILogger logger;

    // Accessed from call-site threads (reads) and the background recreate task (writes).
    // Read/written with Volatile.Read / Volatile.Write to prevent torn reads and to publish
    // the new reference so that callers on other threads observe it without additional
    // synchronization. The TransportState itself is immutable so readers see a consistent
    // (Channel, Invoker) pair once Volatile.Read returns.
    TransportState state;
    int consecutiveFailures;
    int recreateInFlight;
    long lastRecreateTicks;

    public ChannelRecreatingCallInvoker(
        GrpcChannel initialChannel,
        Func<GrpcChannel, CancellationToken, Task<GrpcChannel>> recreator,
        int failureThreshold,
        TimeSpan minRecreateInterval,
        bool ownsChannel,
        ILogger logger)
    {
        this.recreator = recreator;
        this.failureThreshold = failureThreshold;
        this.minRecreateInterval = minRecreateInterval;
        this.ownsChannel = ownsChannel;
        this.logger = logger;
        this.state = new TransportState(initialChannel, initialChannel.CreateCallInvoker());

        // Seed lastRecreateTicks so cooldown does not block the very first recreate attempt.
        this.lastRecreateTicks = Stopwatch.GetTimestamp() - StopwatchTicksFor(minRecreateInterval);
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        TransportState current = Volatile.Read(ref this.state);
        try
        {
            TResponse response = current.Invoker.BlockingUnaryCall(method, host, options, request);
            this.RecordSuccess();
            return response;
        }
        catch (RpcException ex)
        {
            this.RecordFailure(ex.StatusCode, method.FullName);
            throw;
        }
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        TransportState current = Volatile.Read(ref this.state);
        AsyncUnaryCall<TResponse> call = current.Invoker.AsyncUnaryCall(method, host, options, request);
        this.ObserveOutcome(call.ResponseAsync, method.FullName);
        return call;
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        // Streaming calls are forwarded without outcome observation. The streaming methods used by the
        // DurableTask client are bounded snapshots (e.g. StreamInstanceHistory) where errors surface as
        // exceptions to user code, so global failure counting on these would create false positives.
        return Volatile.Read(ref this.state).Invoker.AsyncServerStreamingCall(method, host, options, request);
    }

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options)
    {
        return Volatile.Read(ref this.state).Invoker.AsyncClientStreamingCall(method, host, options);
    }

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options)
    {
        return Volatile.Read(ref this.state).Invoker.AsyncDuplexStreamingCall(method, host, options);
    }

    public async ValueTask DisposeAsync()
    {
        if (!this.ownsChannel)
        {
            return;
        }

        TransportState current = Volatile.Read(ref this.state);
        try
        {
#if NET6_0_OR_GREATER
            await current.Channel.ShutdownAsync().ConfigureAwait(false);
            current.Channel.Dispose();
#else
            await current.Channel.ShutdownAsync().ConfigureAwait(false);
#endif
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // Best-effort disposal.
        }
    }

    static long StopwatchTicksFor(TimeSpan ts) =>
        (long)(ts.TotalSeconds * Stopwatch.Frequency);

    void ObserveOutcome<TResponse>(Task<TResponse> responseAsync, string methodFullName)
    {
        // Use ContinueWith with TaskScheduler.Default so we don't capture sync context.
        responseAsync.ContinueWith(
            (t, state) =>
            {
                var (self, name) = ((ChannelRecreatingCallInvoker, string))state!;
                if (t.Status == TaskStatus.RanToCompletion)
                {
                    self.RecordSuccess();
                }
                else if (t.Exception?.InnerException is RpcException rpcEx)
                {
                    self.RecordFailure(rpcEx.StatusCode, name);
                }
            },
            (this, methodFullName),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    void RecordSuccess()
    {
        Volatile.Write(ref this.consecutiveFailures, 0);
    }

    void RecordFailure(StatusCode status, string methodFullName)
    {
        bool counts = status switch
        {
            StatusCode.Unavailable => true,
            StatusCode.DeadlineExceeded => !DeadlineExceededAllowedMethods.Contains(methodFullName),
            _ => false,
        };

        if (!counts)
        {
            return;
        }

        int count = Interlocked.Increment(ref this.consecutiveFailures);
        if (this.failureThreshold <= 0 || count < this.failureThreshold)
        {
            return;
        }

        this.MaybeTriggerRecreate(count);
    }

    void MaybeTriggerRecreate(int observedCount)
    {
        long nowTicks = Stopwatch.GetTimestamp();
        long elapsedTicks = nowTicks - Volatile.Read(ref this.lastRecreateTicks);
        long cooldownTicks = StopwatchTicksFor(this.minRecreateInterval);
        if (elapsedTicks < cooldownTicks)
        {
            return;
        }

        // Single-flight guard: only one recreate task in flight at a time.
        if (Interlocked.CompareExchange(ref this.recreateInFlight, 1, 0) != 0)
        {
            return;
        }

        // Re-check elapsed under the guard to avoid back-to-back recreates that won the CAS race.
        elapsedTicks = Stopwatch.GetTimestamp() - Volatile.Read(ref this.lastRecreateTicks);
        if (elapsedTicks < cooldownTicks)
        {
            Interlocked.Exchange(ref this.recreateInFlight, 0);
            return;
        }

        this.logger.RecreatingChannel(observedCount);
        _ = Task.Run(() => this.RecreateAsync(observedCount));
    }

    async Task RecreateAsync(int observedCount)
    {
        try
        {
            TransportState current = Volatile.Read(ref this.state);
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
            GrpcChannel newChannel = await this.recreator(current.Channel, cts.Token).ConfigureAwait(false);

            if (!ReferenceEquals(newChannel, current.Channel))
            {
                Volatile.Write(ref this.state, new TransportState(newChannel, newChannel.CreateCallInvoker()));
                this.logger.ChannelRecreated(GetEndpointDescription(newChannel));

                // When we own the channel, no external party is responsible for tearing down the old
                // one. Defer disposal briefly so any in-flight RPCs issued against the previous
                // CallInvoker before the swap can still complete (they already captured the old
                // TransportState before Volatile.Write).
                if (this.ownsChannel)
                {
                    _ = ScheduleDeferredDisposeAsync(current.Channel);
                }
            }

            // Successful recreate (even if a peer beat us to it) → reset the failure counter.
            Volatile.Write(ref this.consecutiveFailures, 0);
            Volatile.Write(ref this.lastRecreateTicks, Stopwatch.GetTimestamp());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException
                                    and not StackOverflowException
                                    and not ThreadAbortException)
        {
            this.logger.ChannelRecreateFailed(ex);

            // Update lastRecreateTicks even on failure so the cooldown applies to failed attempts too.
            Volatile.Write(ref this.lastRecreateTicks, Stopwatch.GetTimestamp());
        }
        finally
        {
            Interlocked.Exchange(ref this.recreateInFlight, 0);
        }
    }

    static async Task ScheduleDeferredDisposeAsync(GrpcChannel channel)
    {
        try
        {
            // Grace period to let in-flight RPCs captured against the old invoker drain before we
            // tear down the channel's HTTP handler / sockets.
            await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            try
            {
                await channel.ShutdownAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                // Expected during process shutdown; nothing to do.
            }
#if NET6_0_OR_GREATER
            channel.Dispose();
#endif
        }
        catch (Exception ex) when (ex is not OutOfMemoryException
                                    and not StackOverflowException
                                    and not ThreadAbortException)
        {
            if (ex is not OperationCanceledException and not ObjectDisposedException)
            {
                Trace.TraceError(
                    "Unexpected exception while deferred-disposing gRPC channel in ChannelRecreatingCallInvoker.ScheduleDeferredDisposeAsync: {0}",
                    ex);
            }
        }
    }

    static string GetEndpointDescription(GrpcChannel channel)
    {
#if NET6_0_OR_GREATER
        return channel.Target ?? "(unknown)";
#else
        return channel.Target ?? "(unknown)";
#endif
    }

    sealed class TransportState
    {
        public TransportState(GrpcChannel channel, CallInvoker invoker)
        {
            this.Channel = channel;
            this.Invoker = invoker;
        }

        public GrpcChannel Channel { get; }

        public CallInvoker Invoker { get; }
    }
}
