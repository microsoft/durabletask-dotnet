// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Represents the current state of a schedule.
/// </summary>
class ScheduleState
{
    const int MaxActivityLogItems = 10;
    readonly Queue<ScheduleActivityLog> activityLogs = new();

    /// <summary>
    /// Gets or sets the current status of the schedule.
    /// </summary>
    public ScheduleStatus Status { get; set; } = ScheduleStatus.Uninitialized;

    /// <summary>
    /// Gets or sets the execution token used to validate schedule operations.
    /// </summary>
    public string ExecutionToken { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the last time the schedule was run.
    /// </summary>
    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>
    /// Gets or sets the next scheduled run time.
    /// </summary>
    public DateTimeOffset? NextRunAt { get; set; }

    /// <summary>
    /// Gets or sets the schedule configuration.
    /// </summary>
    public ScheduleConfiguration? ScheduleConfiguration { get; set; }

    /// <summary>
    /// Gets the activity logs for this schedule.
    /// </summary>
    public IReadOnlyCollection<ScheduleActivityLog> ActivityLogs => this.activityLogs.ToList().AsReadOnly();

    /// <summary>
    /// Refreshes the execution token to invalidate pending schedule operations.
    /// </summary>
    public void RefreshScheduleRunExecutionToken()
    {
        this.ExecutionToken = Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Adds an activity log entry to the schedule's history.
    /// </summary>
    /// <param name="operation">The operation being performed.</param>
    /// <param name="status">The status of the operation.</param>
    /// <param name="failureDetails">Optional failure details if the operation failed.</param>
    public void AddActivityLog(string operation, string status, FailureDetails? failureDetails = null)
    {
        ScheduleActivityLog log = new ScheduleActivityLog
        {
            Operation = operation,
            Status = status,
            Timestamp = DateTimeOffset.UtcNow,
            FailureDetails = failureDetails,
        };

        this.activityLogs.Enqueue(log);

        // Keep only the most recent MaxActivityLogItems
        while (this.activityLogs.Count > MaxActivityLogItems)
        {
            this.activityLogs.Dequeue();
        }
    }
}
