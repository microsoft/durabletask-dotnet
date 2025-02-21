// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Exception thrown when a schedule operation fails.
/// </summary>
public class ScheduleOperationFailedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleOperationFailedException"/> class.
    /// </summary>
    /// <param name="schedule">The schedule that failed.</param>
    public ScheduleOperationFailedException(ScheduleDescription schedule)
        : base($"Operation failed for schedule '{schedule.ScheduleId}'. Refer to schedule details {schedule.ToJsonString()} for more information.")
    {
        this.Schedule = schedule;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleOperationFailedException"/> class.
    /// </summary>
    /// <param name="schedule">The schedule that failed.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ScheduleOperationFailedException(ScheduleDescription schedule, Exception innerException)
        : base($"Operation failed for schedule '{schedule.ScheduleId}'. Refer to schedule details {schedule.ToJsonString()} for more information.", innerException)
    {
        this.Schedule = schedule;
    }

    /// <summary>
    /// Gets the schedule that failed.
    /// </summary>
    public ScheduleDescription Schedule { get; }

    /// <summary>
    /// Gets the ID of the schedule that failed.
    /// </summary>
    public string ScheduleId => this.Schedule.ScheduleId;
}
