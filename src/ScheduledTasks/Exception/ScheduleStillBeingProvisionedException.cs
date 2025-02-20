// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Exception thrown when attempting to perform an operation on a schedule that is still being provisioned.
/// </summary>
public class ScheduleStillBeingProvisionedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleStillBeingProvisionedException"/> class.
    /// </summary>
    /// <param name="scheduleId">The ID of the schedule that is still being provisioned.</param>
    public ScheduleStillBeingProvisionedException(string scheduleId)
        : base($"Schedule '{scheduleId}' is still being provisioned.")
    {
        this.ScheduleId = scheduleId;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleStillBeingProvisionedException"/> class.
    /// </summary>
    /// <param name="scheduleId">The ID of the schedule that is still being provisioned.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ScheduleStillBeingProvisionedException(string scheduleId, Exception innerException)
        : base($"Schedule '{scheduleId}' is still being provisioned.", innerException)
    {
        this.ScheduleId = scheduleId;
    }

    /// <summary>
    /// Gets the ID of the schedule that is still being provisioned.
    /// </summary>
    public string ScheduleId { get; }
}
