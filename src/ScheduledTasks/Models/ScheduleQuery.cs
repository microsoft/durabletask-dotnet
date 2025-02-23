// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Represents query parameters for filtering schedules.
/// </summary>
public record ScheduleQuery
{
    /// <summary>
    /// The default page size when not supplied.
    /// </summary>
    public const int DefaultPageSize = 100;

    /// <summary>
    /// Gets a value indicating whether to include full activity logs in the returned schedules.
    /// </summary>
    public bool IncludeFullActivityLogs { get; init; }

    /// <summary>
    /// Gets the filter for the schedule status.
    /// </summary>
    public ScheduleStatus? Status { get; init; }

    /// <summary>
    /// Gets the prefix to filter schedule IDs.
    /// </summary>
    public string? ScheduleIdPrefix { get; init; }

    /// <summary>
    /// Gets the filter for schedules created after this time.
    /// </summary>
    public DateTimeOffset? CreatedFrom { get; init; }

    /// <summary>
    /// Gets the filter for schedules created before this time.
    /// </summary>
    public DateTimeOffset? CreatedTo { get; init; }

    /// <summary>
    /// Gets a value indicating whether to return only schedule IDs without additional details.
    /// </summary>
    public bool ReturnIdsOnly { get; init; }

    /// <summary>
    /// Gets the maximum number of schedules to return per page.
    /// </summary>
    public int? PageSize { get; init; }

    /// <summary>
    /// Gets the continuation token for retrieving the next page of results.
    /// </summary>
    public string? ContinuationToken { get; init; }
} 