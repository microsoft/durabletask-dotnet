// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Exception thrown when an internal server error occurs while processing a schedule.
/// </summary>
public class ScheduleInternalException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleInternalException"/> class.
    /// </summary>
    /// <param name="scheduleId">The ID of the schedule that encountered the error.</param>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    public ScheduleInternalException(string scheduleId, string message)
        : base($"An internal error occurred while processing schedule '{scheduleId}': {message}")
    {
        this.ScheduleId = scheduleId;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleInternalException"/> class.
    /// </summary>
    /// <param name="scheduleId">The ID of the schedule that encountered the error.</param>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ScheduleInternalException(string scheduleId, string message, Exception innerException)
        : base($"An internal error occurred while processing schedule '{scheduleId}': {message}", innerException)
    {
        this.ScheduleId = scheduleId;
    }

    /// <summary>
    /// Gets the ID of the schedule that encountered the internal error.
    /// </summary>
    public string ScheduleId { get; }
}
