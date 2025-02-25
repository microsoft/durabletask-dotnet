// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Represents a handle to a schedule, allowing operations on a specific schedule instance.
/// </summary>
public abstract class ScheduleClient
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleClient"/> class.
    /// </summary>
    /// <param name="scheduleId">Id of schedule.</param>
    protected ScheduleClient(string scheduleId)
    {
        this.ScheduleId = Check.NotNullOrEmpty(scheduleId, nameof(scheduleId));
    }

    /// <summary>
    /// Gets the ID of this schedule.
    /// </summary>
    public string ScheduleId { get; }

    /// <summary>
    /// Retrieves the current details of this schedule.
    /// </summary>
    /// <param name="cancellation">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that returns the schedule details when complete.</returns>
    public abstract Task<ScheduleDescription> DescribeAsync(CancellationToken cancellation = default);

    /// <summary>
    /// Deletes this schedule.
    /// </summary>
    /// <remarks>
    /// The schedule will stop executing and be removed from the system.
    /// Does not affect orchestrations that have already been started.
    /// </remarks>
    /// <param name="cancellation">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that completes when the schedule has been deleted.</returns>
    public abstract Task DeleteAsync(CancellationToken cancellation = default);

    /// <summary>
    /// Pauses this schedule.
    /// </summary>
    /// <remarks>
    /// The schedule will stop executing but remain in the system.
    /// Does not affect orchestrations that have already been started.
    /// </remarks>
    /// <param name="cancellation">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that completes when the schedule has been paused.</returns>
    public abstract Task PauseAsync(CancellationToken cancellation = default);

    /// <summary>
    /// Resumes this schedule.
    /// </summary>
    /// <remarks>
    /// The schedule will continue executing from where it was paused.
    /// </remarks>
    /// <param name="cancellation">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that completes when the schedule has been resumed.</returns>
    public abstract Task ResumeAsync(CancellationToken cancellation = default);

    /// <summary>
    /// Updates this schedule with new configuration.
    /// </summary>
    /// <remarks>
    /// The schedule will continue executing with the new configuration.
    /// </remarks>
    /// <param name="updateOptions">The options for updating the schedule configuration.</param>
    /// <param name="cancellation">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that completes when the schedule has been updated.</returns>
    public abstract Task UpdateAsync(ScheduleUpdateOptions updateOptions, CancellationToken cancellation = default);
}
