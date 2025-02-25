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
    ScheduleClient GetScheduleClient(string scheduleId);

    /// <summary>
    /// Gets a schedule description by its ID.
    /// </summary>
    /// <param name="scheduleId">The ID of the schedule to retrieve.</param>
    /// <param name="cancellation">Optional cancellation token.</param>
    /// <returns>The schedule description if found, null otherwise.</returns>
    Task<ScheduleDescription?> GetScheduleAsync(string scheduleId, CancellationToken cancellation = default);

    /// <summary>
    /// Gets a pageable list of schedules matching the specified filter criteria.
    /// </summary>
    /// <param name="filter">Optional filter criteria for the schedules. If null, returns all schedules.</param>
    /// <returns>A pageable list of schedule descriptions.</returns>
    AsyncPageable<ScheduleDescription> ListSchedulesAsync(ScheduleQuery? filter = null);

    /// <summary>
    /// Creates a new schedule with the specified configuration.
    /// </summary>
    /// <param name="creationOptions">The options for creating the schedule.</param>
    /// <param name="cancellation">Optional cancellation token.</param>
    /// <returns>A handle to the created schedule.</returns>
    Task<ScheduleClient> CreateScheduleAsync(ScheduleCreationOptions creationOptions, CancellationToken cancellation = default);
}
