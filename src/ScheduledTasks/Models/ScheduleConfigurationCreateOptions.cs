// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Configuration for a scheduled task.
/// </summary>
public class ScheduleConfigurationCreateOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleConfigurationCreateOptions"/> class.
    /// </summary>
    /// <param name="orchestrationName"></param>
    /// <param name="scheduleId"></param>
    public ScheduleConfigurationCreateOptions(string orchestrationName, string scheduleId)
    {
        this.orchestrationName = Check.NotNullOrEmpty(orchestrationName, nameof(orchestrationName));
        this.ScheduleId = scheduleId ?? Guid.NewGuid().ToString("N");
    }

    string orchestrationName;

    public string OrchestrationName
    {
        get => this.orchestrationName;
        set
        {
            this.orchestrationName = Check.NotNullOrEmpty(value, nameof(value));
        }
    }

    public string ScheduleId { get; init; }

    public string? OrchestrationInput { get; set; }

    public string? OrchestrationInstanceId { get; set; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset? StartAt { get; set; }

    public DateTimeOffset? EndAt { get; set; }

    TimeSpan? interval;

    public TimeSpan? Interval
    {
        get => this.interval;
        set
        {
            if (!value.HasValue)
            {
                return;
            }

            if (value.Value <= TimeSpan.Zero)
            {
                throw new ArgumentException("Interval must be positive", nameof(value));
            }

            if (value.Value.TotalSeconds < 1)
            {
                throw new ArgumentException("Interval must be at least 1 second", nameof(value));
            }

            this.interval = value;
        }
    }

    public string? CronExpression { get; set; }

    public int MaxOccurrence { get; set; }

    public bool? StartImmediatelyIfLate { get; set; }
}
