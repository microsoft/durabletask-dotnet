// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Exception thrown when attempting to access a schedule that does not exist.
/// </summary>
public class ScheduleNotFoundException : Exception
{
    /// <summary>
    /// Gets the ID of the schedule that was not found.
    /// </summary>
    public string ScheduleId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleNotFoundException"/> class.
    /// </summary>
    /// <param name="scheduleId">The ID of the schedule that was not found.</param>
    public ScheduleNotFoundException(string scheduleId)
        : base($"Schedule with ID '{scheduleId}' was not found.")
    {
        this.ScheduleId = scheduleId;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleNotFoundException"/> class.
    /// </summary>
    /// <param name="scheduleId">The ID of the schedule that was not found.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ScheduleNotFoundException(string scheduleId, Exception innerException)
        : base($"Schedule with ID '{scheduleId}' was not found.", innerException)
    {
        this.ScheduleId = scheduleId;
    }
}
