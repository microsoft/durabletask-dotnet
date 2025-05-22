// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Dapr.DurableTask.Sidecar;

class AsyncManualResetEvent
{
    readonly object mutex = new();
    TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public AsyncManualResetEvent(bool isSignaled)
    {
        if (isSignaled)
        {
            this.tcs.TrySetCanceled();
        }
    }

    public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        Task delayTask = Task.Delay(timeout, cancellationToken);
        Task waitTask = this.tcs.Task;

        Task winner = await Task.WhenAny(waitTask, delayTask);

        // Await ensures we get a TaskCancelledException if there was a cancellation.
        await winner;

        return winner == waitTask;
    }

    public bool IsSignaled => this.tcs.Task.IsCompleted;

    /// <summary>
    /// Puts the event in the signaled state, unblocking any waiting threads.
    /// </summary>
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
