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
    /// <param name="scheduleId">The ID of the schedule that failed.</param>
    /// <param name="operation">The operation that failed.</param>
    /// <param name="message">The error message that explains the reason for the failure.</param>
    public ScheduleOperationFailedException(string scheduleId, string operation, string message)
        : base($"Operation '{operation}' failed for schedule '{scheduleId}': {message}")
    {
        this.ScheduleId = scheduleId;
        this.Operation = operation;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleOperationFailedException"/> class.
    /// </summary>
    /// <param name="scheduleId">The ID of the schedule that failed.</param>
    /// <param name="operation">The operation that failed.</param>
    /// <param name="message">The error message that explains the reason for the failure.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ScheduleOperationFailedException(string scheduleId, string operation, string message, Exception innerException)
        : base($"Operation '{operation}' failed for schedule '{scheduleId}': {message}", innerException)
    {
        this.ScheduleId = scheduleId;
        this.Operation = operation;
    }

    /// <summary>
    /// Gets the ID of the schedule that failed.
    /// </summary>
    public string ScheduleId { get; }

    /// <summary>
    /// Gets the operation that failed.
    /// </summary>
    public string Operation { get; }
}
