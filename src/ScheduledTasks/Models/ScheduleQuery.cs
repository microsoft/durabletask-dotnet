// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Represents query parameters for filtering schedules.
/// </summary>
public record ScheduleQuery
{
    /// <summary>
    /// Gets or sets a value indicating whether to include full activity logs in the returned schedules.
    /// </summary>
    public bool IncludeFullActivityLogs { get; init; }

    /// <summary>
    /// Gets or sets a filter for the schedule status.
    /// </summary>
    public ScheduleStatus? Status { get; init; }

    /// <summary>
    /// Gets or sets a prefix to filter schedule IDs.
    /// </summary>
    public string? ScheduleIdPrefix { get; init; }

    /// <summary>
    /// Gets or sets a filter for schedules created after this time.
    /// </summary>
    public DateTimeOffset? CreatedAfter { get; init; }

    /// <summary>
    /// Gets or sets a filter for schedules created before this time.
    /// </summary>
    public DateTimeOffset? CreatedBefore { get; init; }

    /// <summary>
    /// Gets or sets the maximum number of schedules to return.
    /// </summary>
    public int? MaxItemCount { get; init; }
} 