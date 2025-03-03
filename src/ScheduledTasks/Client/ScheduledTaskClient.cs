// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Client for managing scheduled tasks.
/// Provides methods to retrieve a ScheduleClient, list all schedules, 
/// and get details of a specific schedule.
/// </summary>
public abstract class ScheduledTaskClient
{
    /// <summary>
    /// Gets a handle to a schedule, allowing operations on it.
    /// </summary>
    /// <param name="scheduleId">The ID of the schedule.</param>
    /// <returns>A handle to manage the schedule.</returns>
    public abstract ScheduleClient GetScheduleClient(string scheduleId);

    /// <summary>
    /// Gets a schedule description by its ID.
    /// </summary>
    /// <param name="scheduleId">The ID of the schedule to retrieve.</param>
    /// <param name="cancellation">Optional cancellation token.</param>
    /// <returns>The schedule description if found, null otherwise.</returns>
    public abstract Task<ScheduleDescription?> GetScheduleAsync(string scheduleId, CancellationToken cancellation = default);

    /// <summary>
    /// Gets a pageable list of schedules matching the specified filter criteria.
    /// </summary>
    /// <param name="filter">Optional filter criteria for the schedules. If null, returns all schedules.</param>
    /// <returns>A pageable list of schedule descriptions.</returns>
    public abstract AsyncPageable<ScheduleDescription> ListSchedulesAsync(ScheduleQuery? filter = null);

    /// <summary>
    /// Creates a new schedule with the specified configuration.
    /// </summary>
    /// <param name="creationOptions">The options for creating the schedule.</param>
    /// <param name="cancellation">Optional cancellation token.</param>
    /// <returns>A ScheduleClient for the created schedule.</returns>
    public abstract Task<ScheduleClient> CreateScheduleAsync(ScheduleCreationOptions creationOptions, CancellationToken cancellation = default);
}
