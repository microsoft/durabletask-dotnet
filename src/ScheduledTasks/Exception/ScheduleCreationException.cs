// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Exception thrown when schedule creation fails.
/// </summary>
public class ScheduleCreationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleCreationException"/> class.
    /// </summary>
    /// <param name="scheduleId">The ID of the schedule that failed to create.</param>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ScheduleCreationException(string scheduleId, string message, Exception? innerException = null)
        : base($"Failed to create schedule '{scheduleId}': {message}", innerException)
    {
        this.ScheduleId = scheduleId;
    }

    /// <summary>
    /// Gets the ID of the schedule that failed to create.
    /// </summary>
    public string ScheduleId { get; }
} 