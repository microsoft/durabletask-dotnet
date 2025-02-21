// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Represents a log entry for schedule activity.
/// </summary>
public record ScheduleActivityLog
{
    /// <summary>
    /// Gets the operation performed.
    /// </summary>
    public string Operation { get; init; } = string.Empty;

    /// <summary>
    /// Gets the status of the operation.
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Gets the timestamp when the operation occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Gets the failure details if the operation failed.
    /// </summary>
    public FailureDetails? FailureDetails { get; init; }
}

/// <summary>
/// Represents details about a failure that occurred during schedule execution.
/// </summary>
public record FailureDetails
{
    /// <summary>
    /// Gets the reason for the failure.
    /// </summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// Gets the type of failure.
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Gets when the failure occurred.
    /// </summary>
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>
    /// Gets the suggested fix for the failure.
    /// </summary>
    public string? SuggestedFix { get; init; }
}
