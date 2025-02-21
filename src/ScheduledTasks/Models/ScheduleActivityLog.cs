// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Represents a log entry for schedule activity.
/// </summary>
public class ScheduleActivityLog
{
    /// <summary>
    /// Gets or sets the operation performed.
    /// </summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the status of the operation.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the timestamp when the operation occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the failure details if the operation failed.
    /// </summary>
    public FailureDetails? FailureDetails { get; set; }
}

/// <summary>
/// Represents details about a failure that occurred during schedule execution.
/// </summary>
public class FailureDetails
{
    /// <summary>
    /// Gets or sets the reason for the failure.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type of failure.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the failure occurred.
    /// </summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>
    /// Gets or sets the suggested fix for the failure.
    /// </summary>
    public string? SuggestedFix { get; set; }
} 