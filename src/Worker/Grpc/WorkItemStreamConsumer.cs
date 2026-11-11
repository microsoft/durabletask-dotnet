// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Worker.Grpc.Internal;
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

#if NET8_0_OR_GREATER
    /// <summary>Consumes a work-item stream until shutdown, silent disconnect, or graceful drain.</summary>
    /// <param name="stream">The response reader.</param>
    /// <param name="silentDisconnectTimeout">Transport inactivity timeout; non-positive values disable detection.</param>
    /// <param name="onItem">Synchronous dispatch after successful payload resolution.</param>
    /// <param name="onFirstMessage">Invoked once on the first successful transport receipt.</param>
    /// <param name="cancellation">Worker shutdown cancellation.</param>
    /// <param name="timeProvider">Clock used for read deadlines.</param>
    /// <param name="onPayloadFailure">Delivery recovery using worker cancellation; its errors propagate to the caller.</param>
    /// <returns>The termination outcome and whether a message was received.</returns>
#else
    /// <summary>Consumes a work-item stream until shutdown, silent disconnect, or graceful drain.</summary>
    /// <param name="stream">The response reader.</param>
    /// <param name="silentDisconnectTimeout">Transport inactivity timeout; non-positive values disable detection.</param>
    /// <param name="onItem">Synchronous dispatch after successful payload resolution.</param>
    /// <param name="onFirstMessage">Invoked once on the first successful transport receipt.</param>
    /// <param name="cancellation">Worker shutdown cancellation.</param>
    /// <param name="onPayloadFailure">Delivery recovery using worker cancellation; its errors propagate to the caller.</param>
    /// <returns>The termination outcome and whether a message was received.</returns>
#endif
    public static async Task<WorkItemStreamResult> ConsumeAsync(
        IAsyncStreamReader<P.WorkItem> stream,
        TimeSpan silentDisconnectTimeout,
        Action<P.WorkItem> onItem,
        Action? onFirstMessage,
        CancellationToken cancellation,
#if NET8_0_OR_GREATER
        TimeProvider? timeProvider = null,
#endif
        Func<WorkItemPayloadException, CancellationToken, Task>? onPayloadFailure = null)
    {
        bool silentDisconnectEnabled = silentDisconnectTimeout > TimeSpan.Zero;

        // Use a fresh deadline for each read. Successful transport receipt wins even if its
        // timeout fires before the awaiting continuation runs; it must not poison later reads.
        TimeSpan effectiveTimeout = ClampCancelAfterTimeout(silentDisconnectTimeout);

        bool firstMessageObserved = false;
        IWorkItemStreamReader? payloadReader = stream as IWorkItemStreamReader;

        void ObserveMessage()
        {
            if (!firstMessageObserved)
            {
                firstMessageObserved = true;
                onFirstMessage?.Invoke();
            }
        }

        try
        {
            while (true)
            {
#if NET8_0_OR_GREATER
                using CancellationTokenSource timeoutSource = new(
                    silentDisconnectEnabled ? effectiveTimeout : Timeout.InfiniteTimeSpan,
                    timeProvider ?? TimeProvider.System);
#else
                using CancellationTokenSource timeoutSource = new(
                    silentDisconnectEnabled ? effectiveTimeout : Timeout.InfiniteTimeSpan);
#endif
                using CancellationTokenSource tokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellation, timeoutSource.Token);
                void OnMessageReceived()
                {
                    timeoutSource.CancelAfter(Timeout.InfiniteTimeSpan);
                    ObserveMessage();
                }

                try
                {
                    bool hasNext = payloadReader is null
                        ? await stream.MoveNext(tokenSource.Token).ConfigureAwait(false)
                        : await payloadReader.MoveNextAsync(OnMessageReceived, tokenSource.Token, cancellation).ConfigureAwait(false);
                    if (!hasNext)
                    {
                        break;
                    }

                    OnMessageReceived();
                }
                catch (WorkItemPayloadException ex) when (onPayloadFailure is not null)
                {
                    OnMessageReceived();
                    await onPayloadFailure(ex, cancellation).ConfigureAwait(false);
                    continue;
                }
                catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellation.IsCancellationRequested)
                {
                    return new WorkItemStreamResult(WorkItemStreamOutcome.SilentDisconnect, firstMessageObserved);
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled
                    && timeoutSource.IsCancellationRequested && !cancellation.IsCancellationRequested)
                {
                    return new WorkItemStreamResult(WorkItemStreamOutcome.SilentDisconnect, firstMessageObserved);
                }

                onItem(stream.Current);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Worker is shutting down.
        }

        if (cancellation.IsCancellationRequested)
        {
            return new WorkItemStreamResult(WorkItemStreamOutcome.Shutdown, firstMessageObserved);
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
