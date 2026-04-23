// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc;

/// <summary>
/// Reason a <see cref="WorkItemStreamConsumer.ConsumeAsync"/> invocation terminated.
/// </summary>
internal enum WorkItemStreamOutcome
{
    /// <summary>The outer cancellation token was signalled (worker shutdown).</summary>
    Shutdown,

    /// <summary>The silent-disconnect timer fired (no item or health ping arrived in time).</summary>
    SilentDisconnect,

    /// <summary>The stream completed without exception (e.g. server-initiated graceful close).</summary>
    GracefulDrain,
}

/// <summary>Result of consuming a work-item stream.</summary>
/// <param name="Outcome">Why the loop terminated.</param>
/// <param name="FirstMessageObserved">Whether at least one message was delivered before termination.</param>
internal readonly record struct WorkItemStreamResult(WorkItemStreamOutcome Outcome, bool FirstMessageObserved);

/// <summary>
/// Consumes a work-item stream and classifies its termination. Owns the silent-disconnect timeout
/// wiring and the catch chain that distinguishes a wedged stream (silent disconnect) from a normal
/// graceful drain or a worker shutdown. Per-item dispatch is delegated to the caller via the
/// <c>onItem</c> callback.
/// </summary>
/// <remarks>
/// The <c>onItem</c> callback is synchronous because production dispatch is fire-and-forget.
/// </remarks>
internal static class WorkItemStreamConsumer
{
    // Stay just below the historical CancelAfter(TimeSpan) ceiling so extremely large configuration
    // values are still treated as "effectively infinite" without depending on framework-specific edge cases.
    static readonly TimeSpan MaxSupportedCancelAfterTimeout = TimeSpan.FromMilliseconds(int.MaxValue - 1d);

    /// <summary>
    /// Consume a work-item stream until shutdown, silent disconnect, or graceful drain.
    /// </summary>
    /// <param name="openStream">
    /// Factory that opens the stream with the supplied linked-cancellation token. Production passes
    /// <c>ct => stream.ResponseStream.ReadAllAsync(ct)</c>; tests pass arbitrary fakes.
    /// </param>
    /// <param name="silentDisconnectTimeout">
    /// How long to wait between successive items (or health pings) before treating the stream as
    /// silently disconnected. Non-positive values disable detection entirely.
    /// </param>
    /// <param name="onItem">
    /// Synchronous per-item dispatch. Invoked once per delivered work item, after the silent-disconnect
    /// timer has been re-armed.
    /// </param>
    /// <param name="onFirstMessage">
    /// Optional callback invoked exactly once when the first message is observed. Used by callers to
    /// reset retry counters that should only count consecutive transport failures.
    /// </param>
    /// <param name="cancellation">Outer worker cancellation token.</param>
    /// <returns>The classified outcome plus whether any message was observed.</returns>
    public static async Task<WorkItemStreamResult> ConsumeAsync(
        Func<CancellationToken, IAsyncEnumerable<P.WorkItem>> openStream,
        TimeSpan silentDisconnectTimeout,
        Action<P.WorkItem> onItem,
        Action? onFirstMessage,
        CancellationToken cancellation)
    {
        bool silentDisconnectEnabled = silentDisconnectTimeout > TimeSpan.Zero;

        // Clamp enormous values once up-front so the timer-reset path can simply re-arm the same window.
        TimeSpan effectiveTimeout = ClampCancelAfterTimeout(silentDisconnectTimeout);

        using CancellationTokenSource timeoutSource = new();
        void ArmSilentDisconnectTimer()
        {
            if (silentDisconnectEnabled)
            {
                timeoutSource.CancelAfter(effectiveTimeout);
            }
        }

        // Arm once before reading so the initial gap before the first message is also bounded.
        ArmSilentDisconnectTimer();

        using CancellationTokenSource tokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellation, timeoutSource.Token);

        bool firstMessageObserved = false;

        try
        {
            await foreach (P.WorkItem workItem in openStream(tokenSource.Token).ConfigureAwait(false))
            {
                ArmSilentDisconnectTimer();

                if (!firstMessageObserved)
                {
                    firstMessageObserved = true;
                    onFirstMessage?.Invoke();
                }

                onItem(workItem);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Worker is shutting down.
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            // Silent-disconnect timer fired and grpc-dotnet surfaced cancellation as OCE
            // (when GrpcChannelOptions.ThrowOperationCanceledOnCancellation == true).
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled
            && timeoutSource.IsCancellationRequested
            && !cancellation.IsCancellationRequested)
        {
            // Silent-disconnect timer fired mid-MoveNext. By default
            // (GrpcChannelOptions.ThrowOperationCanceledOnCancellation == false), grpc-dotnet
            // surfaces the linked cancellation as RpcException(Cancelled) rather than OCE.
            // Without this catch the exception would propagate past the silent-disconnect
            // branch and the recreate path would never fire.
        }

        if (cancellation.IsCancellationRequested)
        {
            return new WorkItemStreamResult(WorkItemStreamOutcome.Shutdown, firstMessageObserved);
        }

        if (timeoutSource.IsCancellationRequested)
        {
            return new WorkItemStreamResult(WorkItemStreamOutcome.SilentDisconnect, firstMessageObserved);
        }

        return new WorkItemStreamResult(WorkItemStreamOutcome.GracefulDrain, firstMessageObserved);
    }

    static TimeSpan ClampCancelAfterTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return timeout;
        }

        return timeout <= MaxSupportedCancelAfterTimeout
            ? timeout
            : MaxSupportedCancelAfterTimeout;
    }
}
