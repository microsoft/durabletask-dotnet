// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Exception thrown when attempting to create a schedule with an ID that already exists.
/// </summary>
public class ScheduleAlreadyExistsException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleAlreadyExistsException"/> class.
    /// </summary>
    /// <param name="scheduleId">The ID of the schedule that already exists.</param>
    public ScheduleAlreadyExistsException(string scheduleId)
        : base($"A schedule with ID '{scheduleId}' already exists.")
    {
        this.ScheduleId = scheduleId;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleAlreadyExistsException"/> class.
    /// </summary>
    /// <param name="scheduleId">The ID of the schedule that already exists.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ScheduleAlreadyExistsException(string scheduleId, Exception innerException)
        : base($"A schedule with ID '{scheduleId}' already exists.", innerException)
    {
        this.ScheduleId = scheduleId;
    }

    /// <summary>
    /// Gets the ID of the schedule that already exists.
    /// </summary>
    public string ScheduleId { get; }
}
