// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Interface for managing scheduled tasks in a Durable Task application.
/// </summary>
public interface IScheduledTaskClient
{
    /// <summary>
    /// Gets the current state of a schedule.
    /// </summary>
    /// <param name="scheduleId">The ID of the schedule to retrieve.</param>
    /// <returns>The current state of the schedule.</returns>
    Task<ScheduleState> GetScheduleAsync(string scheduleId);

    /// <summary>
    /// Gets a list of all schedule IDs.
    /// </summary>
    /// <returns>A list of schedule IDs.</returns>
    Task<IEnumerable<string>> ListSchedulesAsync();

    /// <summary>
    /// Creates a new schedule with the specified configuration.
    /// </summary>
    /// <returns>The ID of the newly created schedule.</returns>
    Task<string> CreateScheduleAsync(ScheduleCreationOptions scheduleConfigCreateOptions);

    /// <summary>
    /// Deletes an existing schedule.
    /// </summary>
    /// <param name="scheduleId">The ID of the schedule to delete.</param>
    /// <returns>A task that completes when the schedule is deleted.</returns>
    Task DeleteScheduleAsync(string scheduleId);

    /// <summary>
    /// Pauses an active schedule.
    /// </summary>
    /// <param name="scheduleId">The ID of the schedule to pause.</param>
    /// <returns>A task that completes when the schedule is paused.</returns>
    Task PauseScheduleAsync(string scheduleId);

    /// <summary>
    /// Resumes a paused schedule.
    /// </summary>
    /// <param name="scheduleId">The ID of the schedule to resume.</param>
    /// <returns>A task that completes when the schedule is resumed.</returns>
    Task ResumeScheduleAsync(string scheduleId);

    /// <summary>
    /// Updates an existing schedule with new configuration.
    /// </summary>
    /// <param name="scheduleId">The ID of the schedule to update.</param>
    /// <param name="scheduleConfigurationUpdateOptions">The options for updating the schedule configuration.</param>
    /// <returns>A task that completes when the schedule is updated.</returns>
    Task UpdateScheduleAsync(string scheduleId, ScheduleUpdateOptions scheduleConfigurationUpdateOptions);
}
