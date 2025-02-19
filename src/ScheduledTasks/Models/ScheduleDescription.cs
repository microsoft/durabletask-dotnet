// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Represents the comprehensive details of a schedule.
/// </summary>
public record ScheduleDescription(
    string ScheduleId,
    string OrchestrationName,
    string? OrchestrationInput,
    string? OrchestrationInstanceId,
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    TimeSpan? Interval,
    string? CronExpression,
    int MaxOccurrence,
    bool? StartImmediatelyIfLate,
    ScheduleStatus Status,
    string ExecutionToken,
    DateTimeOffset? LastRunAt,
    DateTimeOffset? NextRunAt
);
