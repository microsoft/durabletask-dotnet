// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Exception thrown when an invalid state transition is attempted on a schedule.
/// </summary>
public class ScheduleInvalidTransitionException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleInvalidTransitionException"/> class.
    /// </summary>
    /// <param name="scheduleId">The ID of the schedule on which the invalid transition was attempted.</param>
    /// <param name="fromStatus">The current status of the schedule.</param>
    /// <param name="toStatus">The target status that was invalid.</param>
    /// <param name="operationName">The name of the operation that was attempted.</param>
    public ScheduleInvalidTransitionException(string scheduleId, ScheduleStatus fromStatus, ScheduleStatus toStatus, string operationName)
        : base($"Invalid state transition attempted for schedule '{scheduleId}': Cannot transition from {fromStatus} to {toStatus} during {operationName} operation.")
    {
        this.ScheduleId = scheduleId;
        this.FromStatus = fromStatus;
        this.ToStatus = toStatus;
        this.OperationName = operationName;
    }

    /// <summary>
    /// Gets the ID of the schedule that encountered the invalid transition.
    /// </summary>
    public string ScheduleId { get; }

    /// <summary>
    /// Gets the status the schedule was transitioning from.
    /// </summary>
    public ScheduleStatus FromStatus { get; }

    /// <summary>
    /// Gets the invalid target status that was attempted.
    /// </summary>
    public ScheduleStatus ToStatus { get; }

    /// <summary>
    /// Gets the name of the operation that was attempted.
    /// </summary>
    public string OperationName { get; }
}
