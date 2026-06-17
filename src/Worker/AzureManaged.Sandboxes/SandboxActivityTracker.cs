// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.AzureManaged.Sandboxes;

/// <summary>
/// Tracks activity execution state for an on-demand sandbox worker process.
/// </summary>
sealed class SandboxActivityTracker
{
    int activeActivityCount;

    /// <summary>
    /// Gets the number of activities currently in flight on this worker.
    /// </summary>
    public int InFlightCount => Volatile.Read(ref this.activeActivityCount);

    /// <summary>
    /// Records the start of an in-flight activity.
    /// </summary>
    internal void NotifyActivityStarted() => Interlocked.Increment(ref this.activeActivityCount);

    /// <summary>
    /// Records the completion of an activity.
    /// </summary>
    internal void NotifyActivityCompleted()
    {
        while (true)
        {
            int currentCount = Volatile.Read(ref this.activeActivityCount);
            if (currentCount == 0)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref this.activeActivityCount, currentCount - 1, currentCount) == currentCount)
            {
                return;
            }
        }
    }
}
