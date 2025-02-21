// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Interface for managing scheduled tasks in a Durable Task application.
/// </summary>
public interface IScheduledTaskClient
{
    /// <summary>
    /// Gets a handle to a schedule, allowing operations on it.
    /// </summary>
    /// <param name="scheduleId">The ID of the schedule.</param>
    /// <returns>A handle to manage the schedule.</returns>
    IScheduleHandle GetScheduleHandle(string scheduleId);

    /// <summary>
    /// Gets a list of all initialized schedules.
    /// </summary>
    /// <param name="includeFullActivityLogs">Whether to include full activity logs in the returned schedules.</param>
    /// <returns>A list of schedule descriptions.</returns>
    Task<IEnumerable<ScheduleDescription>> ListSchedulesAsync(bool includeFullActivityLogs);

    /// <summary>
    /// Creates a new schedule with the specified configuration.
    /// </summary>
    /// <param name="scheduleConfigCreateOptions">The configuration options for creating the schedule.</param>
    /// <returns>The ID of the newly created schedule.</returns>
    Task<IScheduleHandle> CreateScheduleAsync(ScheduleCreationOptions scheduleConfigCreateOptions);
}
