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
    /// <returns>The schedule details.</returns>
    Task<ScheduleDescription> DescribeAsync();

    /// <summary>
    /// Deletes this schedule.
    /// </summary>
    /// <returns>A task that completes when the schedule is deleted.</returns>
    Task<IScheduleWaiter> DeleteAsync();

    /// <summary>
    /// Pauses this schedule.
    /// </summary>
    /// <returns>A task that completes when the schedule is paused.</returns>
    Task<IScheduleWaiter> PauseAsync();

    /// <summary>
    /// Resumes this schedule.
    /// </summary>
    /// <returns>A task that completes when the schedule is resumed.</returns>
    Task<IScheduleWaiter> ResumeAsync();

    /// <summary>
    /// Updates this schedule with new configuration.
    /// </summary>
    /// <param name="updateOptions">The options for updating the schedule configuration.</param>
    /// <returns>A task that completes when the schedule is updated.</returns>
    Task<IScheduleWaiter> UpdateAsync(ScheduleUpdateOptions updateOptions);
}
