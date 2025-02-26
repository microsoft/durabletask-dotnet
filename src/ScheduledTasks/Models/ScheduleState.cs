// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Represents the current state of a schedule.
/// </summary>
class ScheduleState
{
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
    /// Gets or sets the time when this schedule was created.
    /// </summary>
    public DateTimeOffset? ScheduleCreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the time when this schedule was last modified.
    /// </summary>
    public DateTimeOffset? ScheduleLastModifiedAt { get; set; }

    /// <summary>
    /// Gets or sets the schedule configuration.
    /// </summary>
    public ScheduleConfiguration? ScheduleConfiguration { get; set; }

    /// <summary>
    /// Refreshes the execution token to invalidate pending schedule operations.
    /// </summary>
    public void RefreshScheduleRunExecutionToken()
    {
        this.ExecutionToken = Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Gets or sets the time when this schedule was last paused.
    /// </summary>
    public DateTimeOffset? LastPausedAt { get; set; }

    /// <summary>
    /// Clears all state fields to their default values.
    /// </summary>
    public void ClearState()
    {
        this.Status = ScheduleStatus.Uninitialized;
        this.ExecutionToken = Guid.NewGuid().ToString("N");
        this.LastRunAt = null;
        this.NextRunAt = null;
        this.ScheduleCreatedAt = null;
        this.ScheduleLastModifiedAt = null;
        this.ScheduleConfiguration = null;
        this.LastPausedAt = null;
    }
}