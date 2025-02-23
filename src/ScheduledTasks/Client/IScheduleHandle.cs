// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Represents a handle to a schedule, allowing operations on a specific schedule instance.
/// </summary>
public interface IScheduleHandle
{
    /// <summary>
    /// Gets the ID of this schedule.
    /// </summary>
    string ScheduleId { get; }

    /// <summary>
    /// Retrieves the current details of this schedule.
    /// </summary>
    /// <param name="cancellation">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The schedule details.</returns>
    Task<ScheduleDescription> DescribeAsync(CancellationToken cancellation = default);

    /// <summary>
    /// Deletes this schedule. The schedule will stop executing and be removed from the system.
    /// </summary>
    /// <param name="cancellation">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that completes when the schedule has been deleted.</returns>
    Task DeleteAsync(CancellationToken cancellation = default);

    /// <summary>
    /// Pauses this schedule. The schedule will stop executing but remain in the system.
    /// </summary>
    /// <param name="cancellation">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that completes when the schedule has been paused.</returns>
    Task PauseAsync(CancellationToken cancellation = default);

    /// <summary>
    /// Resumes this schedule. The schedule will continue executing from where it was paused.
    /// </summary>
    /// <param name="cancellation">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that completes when the schedule has been resumed.</returns>
    Task ResumeAsync(CancellationToken cancellation = default);

    /// <summary>
    /// Updates this schedule with new configuration. The schedule will continue executing with the new configuration.
    /// </summary>
    /// <param name="updateOptions">The options for updating the schedule configuration.</param>
    /// <param name="cancellation">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that completes when the schedule has been updated.</returns>
    Task UpdateAsync(ScheduleUpdateOptions updateOptions, CancellationToken cancellation = default);
}
