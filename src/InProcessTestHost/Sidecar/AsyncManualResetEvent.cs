// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Sidecar;

/// <summary>
/// An asynchronous manual reset event implementation.
/// </summary>
/// <remarks>
/// This class provides an asynchronous version of ManualResetEvent that can be used
/// for synchronization in async/await scenarios.
/// </remarks>
class AsyncManualResetEvent
{
    readonly object mutex = new();
    TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncManualResetEvent"/> class.
    /// </summary>
    /// <param name="isSignaled">Whether the event should start in the signaled state.</param>
    public AsyncManualResetEvent(bool isSignaled)
    {
        if (isSignaled)
        {
            this.tcs.TrySetCanceled();
        }
    }

    /// <summary>
    /// Gets a value indicating whether the event is in the signaled state.
    /// </summary>
    public bool IsSignaled => this.tcs.Task.IsCompleted;

    /// <summary>
    /// Waits for the event to be signaled with a timeout.
    /// </summary>
    /// <param name="timeout">The timeout duration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the event was signaled, false if the timeout occurred.</returns>
    public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        Task delayTask = Task.Delay(timeout, cancellationToken);
        Task waitTask = this.tcs.Task;

        Task winner = await Task.WhenAny(waitTask, delayTask);

        // Await ensures we get a TaskCancelledException if there was a cancellation.
        await winner;

        return winner == waitTask;
    }

    /// <summary>
    /// Puts the event in the signaled state, unblocking any waiting threads.
    /// </summary>
    /// <returns>True if result is set.</returns>
    public bool Set()
    {
        lock (this.mutex)
        {
            return this.tcs.TrySetResult();
        }
    }

    /// <summary>
    /// Puts this event into the unsignaled state, causing threads to block.
    /// </summary>
    public void Reset()
    {
        lock (this.mutex)
        {
            if (this.tcs.Task.IsCompleted)
            {
                this.tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }
}
