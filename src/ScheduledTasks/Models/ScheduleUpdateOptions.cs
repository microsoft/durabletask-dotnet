// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

public class ScheduleUpdateOptions
{
    string? orchestrationName;

    public string? OrchestrationName
    {
        get => this.orchestrationName;
        set
        {
            this.orchestrationName = value;
        }
    }

    public string? OrchestrationInput { get; set; }

    public string? OrchestrationInstanceId { get; set; }

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
