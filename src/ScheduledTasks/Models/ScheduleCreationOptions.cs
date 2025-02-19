// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Configuration for a scheduled task.
/// </summary>
public record ScheduleCreationOptions
{
    /// <summary>
    /// The name of the orchestration function to schedule.
    /// </summary>
    public string OrchestrationName { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleCreationOptions"/> class.
    /// </summary>
    /// <param name="orchestrationName">The name of the orchestration function to schedule.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="orchestrationName"/> is null or empty.</exception>
    public ScheduleCreationOptions(string orchestrationName)
    {
        Check.NotNullOrEmpty(orchestrationName, nameof(orchestrationName));
        this.OrchestrationName = orchestrationName;
    }

    /// <summary>
    /// The ID of the schedule, if not provided, default to a new GUID.
    /// </summary>
    public string ScheduleId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// The input to the orchestration function.
    /// </summary>
    public string? OrchestrationInput { get; init; }

    /// <summary>
    /// The instance ID of the orchestration function, if not provided, default to a new GUID.
    /// </summary>
    public string OrchestrationInstanceId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// The start time of the schedule.
    /// </summary>
    public DateTimeOffset? StartAt { get; init; }

    /// <summary>
    /// The end time of the schedule.
    /// </summary>
    public DateTimeOffset? EndAt { get; init; }

    /// <summary>
    /// The interval of the schedule.
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

    // public string? CronExpression { get; init; }

    // public int MaxOccurrence { get; init; }

    // public bool? StartImmediatelyIfLate { get; init; }
}
