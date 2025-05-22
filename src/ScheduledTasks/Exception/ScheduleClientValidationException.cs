// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Dapr.DurableTask.ScheduledTasks;

/// <summary>
/// Exception thrown when client-side validation fails for schedule operations.
/// </summary>
public class ScheduleClientValidationException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleClientValidationException"/> class.
    /// </summary>
    /// <param name="scheduleId">The ID of the schedule that failed validation.</param>
    /// <param name="message">The validation error message.</param>
    public ScheduleClientValidationException(string scheduleId, string message)
        : base($"Validation failed for schedule '{scheduleId}': {message}")
    {
        this.ScheduleId = scheduleId;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleClientValidationException"/> class.
    /// </summary>
    /// <param name="scheduleId">The ID of the schedule that failed validation.</param>
    /// <param name="message">The validation error message.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ScheduleClientValidationException(string scheduleId, string message, Exception innerException)
        : base($"Validation failed for schedule '{scheduleId}': {message}", innerException)
    {
        this.ScheduleId = scheduleId;
    }

    /// <summary>
    /// Gets the ID of the schedule that failed validation.
    /// </summary>
    public string ScheduleId { get; }
}
