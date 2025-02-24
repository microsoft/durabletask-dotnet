// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

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
    /// Gets the ID of the schedule that failed validation.
    /// </summary>
    public string ScheduleId { get; }
}
