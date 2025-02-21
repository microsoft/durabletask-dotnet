// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Configuration for a scheduled task.
/// </summary>
public record ScheduleCreationOptions
{
    /// <summary>
    /// The interval of the schedule.
    /// </summary>
    TimeSpan? interval;

    string orchestrationName = string.Empty;

    /// <summary>
    /// Gets the name of the orchestration function to schedule.
    /// </summary>
    public string OrchestrationName
    {
        get => this.orchestrationName;
        init => this.orchestrationName = Check.NotNullOrEmpty(value, nameof(value));
    }

    /// <summary>
    /// Gets the ID of the schedule, if not provided, default to a new GUID.
    /// </summary>
    public string ScheduleId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets the input to the orchestration function.
    /// </summary>
    public string? OrchestrationInput { get; init; }

    /// <summary>
    /// Gets the instance ID of the orchestration function, if not provided, default to a new GUID.
    /// </summary>
    public string OrchestrationInstanceId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets the start time of the schedule.
    /// </summary>
    public DateTimeOffset? StartAt { get; init; }

    /// <summary>
    /// Gets the end time of the schedule.
    /// </summary>
    public DateTimeOffset? EndAt { get; init; }

    /// <summary>
    /// Gets the interval of the schedule.
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
    /// Gets the cron expression for the schedule.
    /// </summary>
    public string? CronExpression { get; init; }

    /// <summary>
    /// Gets the maximum number of occurrences for the schedule.
    /// </summary>
    public int MaxOccurrence { get; init; }

    /// <summary>
    /// Gets a value indicating whether to start the schedule immediately if it is late.
    /// </summary>
    public bool? StartImmediatelyIfLate { get; init; }
}
