// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Configuration for a scheduled task.
/// </summary>
public record ScheduleCreationOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleCreationOptions"/> class.
    /// </summary>
    /// <param name="scheduleId">The ID of the schedule, or null to generate one.</param>
    /// <param name="orchestrationName">The name of the orchestration to schedule.</param>
    /// <param name="interval">The time interval between schedule executions. Must be at least 1 second and cannot be negative.</param>
    public ScheduleCreationOptions(string scheduleId, string orchestrationName, TimeSpan interval)
    {
        this.ScheduleId = Check.NotNullOrEmpty(scheduleId, nameof(scheduleId));
        this.OrchestrationName = Check.NotNullOrEmpty(orchestrationName, nameof(orchestrationName));
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentException("Interval must be positive", nameof(interval));
        }

        if (interval.TotalSeconds < 1)
        {
            throw new ArgumentException("Interval must be at least 1 second", nameof(interval));
        }

        this.Interval = interval;
    }

    /// <summary>
    /// Gets the name of the orchestration function to schedule.
    /// </summary>
    public string OrchestrationName { get; }

    /// <summary>
    /// Gets the ID of the schedule.
    /// </summary>
    public string ScheduleId { get; }

    /// <summary>
    /// Gets the input to the orchestration function.
    /// </summary>
    public string? OrchestrationInput { get; init; }

    /// <summary>
    /// Gets the instance ID of the orchestration function.
    /// </summary>
    public string? OrchestrationInstanceId { get; init; }

    /// <summary>
    /// Gets the start time of the schedule. If not provided, default to the current time.
    /// </summary>
    public DateTimeOffset? StartAt { get; init; }

    /// <summary>
    /// Gets the end time of the schedule. If not provided, schedule will run indefinitely.
    /// </summary>
    public DateTimeOffset? EndAt { get; init; }

    /// <summary>
    /// Gets the interval of the schedule.
    /// </summary>
    public TimeSpan Interval { get; }

    /// <summary>
    /// Gets a value indicating whether to start the schedule immediately if it is late. Default is false.
    /// </summary>
    public bool StartImmediatelyIfLate { get; init; }
}
