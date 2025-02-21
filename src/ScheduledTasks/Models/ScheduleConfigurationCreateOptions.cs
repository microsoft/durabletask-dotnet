// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Configuration options for creating a scheduled task.
/// </summary>
public class ScheduleConfigurationCreateOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleConfigurationCreateOptions"/> class.
    /// </summary>
    /// <param name="orchestrationName">The name of the orchestration to schedule.</param>
    /// <param name="scheduleId">Optional ID for the schedule. If not provided, a new GUID will be generated.</param>
    public ScheduleConfigurationCreateOptions(string orchestrationName, string scheduleId)
    {
        this.orchestrationName = Check.NotNullOrEmpty(orchestrationName, nameof(orchestrationName));
        this.ScheduleId = scheduleId ?? Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Gets or sets the name of the orchestration to schedule.
    /// </summary>
    public string OrchestrationName
    {
        get => this.orchestrationName;
        set => this.orchestrationName = Check.NotNullOrEmpty(value, nameof(value));
    }

    /// <summary>
    /// Gets the unique identifier for this schedule.
    /// </summary>
    public string ScheduleId { get; init; }

    /// <summary>
    /// Gets or sets the input data to pass to the orchestration.
    /// </summary>
    public string? OrchestrationInput { get; set; }

    /// <summary>
    /// Gets or sets the instance ID for the orchestration. Defaults to a new GUID.
    /// </summary>
    public string? OrchestrationInstanceId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets when the schedule should start.
    /// </summary>
    public DateTimeOffset? StartAt { get; set; }

    /// <summary>
    /// Gets or sets when the schedule should end.
    /// </summary>
    public DateTimeOffset? EndAt { get; set; }

    /// <summary>
    /// Gets or sets the time interval between schedule executions. Must be at least 1 second.
    /// </summary>
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

    /// <summary>
    /// Gets or sets whether to start immediately if the schedule is already late.
    /// </summary>
    public bool? StartImmediatelyIfLate { get; set; }

    string orchestrationName;

    TimeSpan? interval;
}
