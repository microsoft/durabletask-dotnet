// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Provides waiter functionality for schedule state transitions.
/// </summary>
public interface IScheduleWaiter
{
    /// <summary>
    /// Waits until the schedule is paused.
    /// </summary>
    /// <param name="options">Optional wait options to configure timeout, polling intervals and backoff strategy. If not provided, default polling mechanism will be used.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The schedule description once paused.</returns>
    Task<ScheduleDescription> WaitUntilPausedAsync(
        WaitOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits until the schedule is active.
    /// </summary>
    /// <param name="options">Optional wait options to configure timeout, polling intervals and backoff strategy. If not provided, default polling mechanism will be used.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The schedule description once active.</returns>
    Task<ScheduleDescription> WaitUntilActiveAsync(
        WaitOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits until the schedule is deleted.
    /// </summary>
    /// <param name="options">Optional wait options to configure timeout, polling intervals and backoff strategy. If not provided, default polling mechanism will be used.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>True if the schedule was deleted, false otherwise.</returns>
    Task<bool> WaitUntilDeletedAsync(
        WaitOptions? options = null,
        CancellationToken cancellationToken = default);
}
