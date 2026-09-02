// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Represents the state of an extended session for an orchestration.
/// </summary>
public class ExtendedSessionState
{
    const long DisposedOwnership = -1;
    const long RunnerOwnership = 0;

    long nextCacheGeneration;
    long ownership = RunnerOwnership;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtendedSessionState"/> class.
    /// </summary>
    /// <param name="state">The orchestration's runtime state.</param>
    /// <param name="taskOrchestration">The TaskOrchestration implementation of the orchestration.</param>
    /// <param name="orchestrationExecutor">The TaskOrchestrationExecutor for the orchestration.</param>
    public ExtendedSessionState(OrchestrationRuntimeState state, TaskOrchestration taskOrchestration, TaskOrchestrationExecutor orchestrationExecutor)
    {
        this.RuntimeState = state;
        this.TaskOrchestration = taskOrchestration;
        this.OrchestrationExecutor = orchestrationExecutor;
    }

    /// <summary>
    /// Gets or sets the saved runtime state of the orchestration.
    /// </summary>
    public OrchestrationRuntimeState RuntimeState { get; set; }

    /// <summary>
    /// Gets or sets the saved TaskOrchestration implementation of the orchestration.
    /// </summary>
    public TaskOrchestration TaskOrchestration { get; set; }

    /// <summary>
    /// Gets or sets the saved TaskOrchestrationExecutor.
    /// </summary>
    public TaskOrchestrationExecutor OrchestrationExecutor { get; set; }

    /// <summary>
    /// Attempts to transfer ownership from a runner to a new cache generation.
    /// </summary>
    /// <param name="generation">The new cache generation.</param>
    /// <returns><c>true</c> if ownership was transferred; otherwise, <c>false</c>.</returns>
    internal bool TryTransferToCache(out long generation)
    {
        generation = Interlocked.Increment(ref this.nextCacheGeneration);
        if (generation <= RunnerOwnership)
        {
            throw new InvalidOperationException("The extended-session cache generation overflowed.");
        }

        return Interlocked.CompareExchange(ref this.ownership, generation, RunnerOwnership)
            == RunnerOwnership;
    }

    /// <summary>
    /// Attempts to transfer ownership from the current cache generation to a runner.
    /// </summary>
    /// <param name="generation">The cache generation that previously owned the session.</param>
    /// <returns><c>true</c> if ownership was transferred; otherwise, <c>false</c>.</returns>
    internal bool TryTakeFromCache(out long generation)
    {
        while (true)
        {
            generation = Volatile.Read(ref this.ownership);
            if (generation == RunnerOwnership)
            {
                // Values inserted directly through the public MemoryCache API predate generation tracking.
                return true;
            }

            if (generation == DisposedOwnership)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref this.ownership, RunnerOwnership, generation) == generation)
            {
                return true;
            }
        }
    }

    /// <summary>
    /// Attempts to reclaim a specific cache generation after a failed insertion.
    /// </summary>
    /// <param name="generation">The cache generation to reclaim.</param>
    /// <returns><c>true</c> if ownership returned to the runner; otherwise, <c>false</c>.</returns>
    internal bool TryTakeCacheGeneration(long generation)
    {
        return generation > RunnerOwnership
            && Interlocked.CompareExchange(ref this.ownership, RunnerOwnership, generation) == generation;
    }

    /// <summary>
    /// Disposes the session if it is currently owned by a runner.
    /// </summary>
    internal void DisposeRunnerOwned()
    {
        if (Interlocked.CompareExchange(ref this.ownership, DisposedOwnership, RunnerOwnership)
            == RunnerOwnership)
        {
            this.DisposeTaskOrchestration();
        }
    }

    /// <summary>
    /// Disposes the session if it is currently owned by the specified cache generation.
    /// </summary>
    /// <param name="generation">The cache generation requesting disposal.</param>
    internal void DisposeCacheGeneration(long generation)
    {
        if (generation > RunnerOwnership
            && Interlocked.CompareExchange(ref this.ownership, DisposedOwnership, generation) == generation)
        {
            this.DisposeTaskOrchestration();
        }
    }

    void DisposeTaskOrchestration()
    {
        (this.TaskOrchestration as IDisposable)?.Dispose();
    }
}
