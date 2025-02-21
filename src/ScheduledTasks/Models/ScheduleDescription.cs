// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Represents the comprehensive details of a schedule.
/// </summary>
public record ScheduleDescription
{
    /// <summary>
    /// Gets the unique identifier for the schedule.
    /// </summary>
    public string ScheduleId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the name of the orchestration to run.
    /// </summary>
    public string? OrchestrationName { get; init; };

    /// <summary>
    /// Gets the optional input for the orchestration.
    /// </summary>
    public string? OrchestrationInput { get; init; }

    /// <summary>
    /// Gets the optional instance ID for the orchestration.
    /// </summary>
    public string? OrchestrationInstanceId { get; init; }

    /// <summary>
    /// Gets the optional start time for the schedule.
    /// </summary>
    public DateTimeOffset? StartAt { get; init; }

    /// <summary>
    /// Gets the optional end time for the schedule.
    /// </summary>
    public DateTimeOffset? EndAt { get; init; }

    /// <summary>
    /// Gets the optional interval between schedule runs.
    /// </summary>
    public TimeSpan? Interval { get; init; }

    /// <summary>
    /// Gets the optional cron expression for the schedule.
    /// </summary>
    public string? CronExpression { get; init; }

    /// <summary>
    /// Gets the maximum number of times the schedule should run.
    /// </summary>
    public int MaxOccurrence { get; init; }

    /// <summary>
    /// Gets whether the schedule should run immediately if started late.
    /// </summary>
    public bool? StartImmediatelyIfLate { get; init; }

    /// <summary>
    /// Gets the current status of the schedule.
    /// </summary>
    public ScheduleStatus Status { get; init; }

    /// <summary>
    /// Gets the execution token used to validate schedule operations.
    /// </summary>
    public string ExecutionToken { get; init; } = string.Empty;

    /// <summary>
    /// Gets the last time the schedule was run.
    /// </summary>
    public DateTimeOffset? LastRunAt { get; init; }

    /// <summary>
    /// Gets the next scheduled run time.
    /// </summary>
    public DateTimeOffset? NextRunAt { get; init; }

    /// <summary>
    /// Gets the activity logs for this schedule.
    /// </summary>
    public IReadOnlyCollection<ScheduleActivityLog> ActivityLogs { get; init; } = Array.Empty<ScheduleActivityLog>();
}
