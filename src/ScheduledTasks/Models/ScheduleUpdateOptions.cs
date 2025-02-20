// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Options for updating an existing schedule.
/// </summary>
public record ScheduleUpdateOptions
{
    TimeSpan? interval;

    /// <summary>
    /// Gets or initializes the name of the orchestration function to schedule.
    /// </summary>
    public string? OrchestrationName { get; init; }

    /// <summary>
    /// Gets or initializes the input to the orchestration function.
    /// </summary>
    public string? OrchestrationInput { get; init; }

    /// <summary>
    /// Gets or initializes the instance ID of the orchestration function.
    /// </summary>
    public string? OrchestrationInstanceId { get; init; }

    /// <summary>
    /// Gets or initializes the start time of the schedule.
    /// </summary>
    public DateTimeOffset? StartAt { get; init; }

    /// <summary>
    /// Gets or initializes the end time of the schedule.
    /// </summary>
    public DateTimeOffset? EndAt { get; init; }

    /// <summary>
    /// Gets or initializes the interval of the schedule.
    /// </summary>
    public TimeSpan? Interval
    {
        get => this.interval;
        init
        {
            if (value.HasValue)
            {
                if (value.Value <= TimeSpan.Zero)
                {
                    throw new ArgumentException("Interval must be positive", nameof(value));
                }

                if (value.Value.TotalSeconds < 1)
                {
                    throw new ArgumentException("Interval must be at least 1 second", nameof(value));
                }
            }

            this.interval = value;
        }
    }

    /// <summary>
    /// Gets or initializes the cron expression for the schedule.
    /// </summary>
    public string? CronExpression { get; init; }

    /// <summary>
    /// Gets or initializes the maximum number of times the schedule should run.
    /// </summary>
    public int MaxOccurrence { get; init; }

    /// <summary>
    /// Gets or initializes whether the schedule should start immediately if it's late.
    /// </summary>
    public bool? StartImmediatelyIfLate { get; init; }
}
