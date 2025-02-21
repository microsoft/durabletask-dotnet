// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Configuration for a scheduled task.
/// </summary>
class ScheduleConfiguration
{
    string orchestrationName;
    TimeSpan? interval;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleConfiguration"/> class.
    /// </summary>
    /// <param name="orchestrationName">The name of the orchestration to schedule.</param>
    /// <param name="scheduleId">The ID of the schedule, or null to generate one.</param>
    public ScheduleConfiguration(string orchestrationName, string scheduleId)
    {
        this.orchestrationName = Check.NotNullOrEmpty(orchestrationName, nameof(orchestrationName));
        this.ScheduleId = scheduleId ?? Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Gets or sets the name of the orchestration function to schedule.
    /// </summary>
    public string OrchestrationName
    {
        get => this.orchestrationName;
        set
        {
            this.orchestrationName = Check.NotNullOrEmpty(value, nameof(value));
        }
    }

    /// <summary>
    /// Gets the ID of the schedule.
    /// </summary>
    public string ScheduleId { get; init; }

    /// <summary>
    /// Gets or sets the input to the orchestration function.
    /// </summary>
    public string? OrchestrationInput { get; set; }

    /// <summary>
    /// Gets or sets the instance ID of the orchestration function.
    /// </summary>
    public string? OrchestrationInstanceId { get; set; }

    /// <summary>
    /// Gets or sets the start time of the schedule.
    /// </summary>
    public DateTimeOffset? StartAt { get; set; }

    /// <summary>
    /// Gets or sets the end time of the schedule.
    /// </summary>
    public DateTimeOffset? EndAt { get; set; }

    /// <summary>
    /// Gets or sets the interval between schedule executions.
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
    /// Gets or sets whether the schedule should start immediately if it's late.
    /// </summary>
    public bool? StartImmediatelyIfLate { get; set; } = false;

    /// <summary>
    /// Creates a new configuration from the provided creation options.
    /// </summary>
    /// <param name="createOptions">The options to create the configuration from.</param>
    /// <returns>A new schedule configuration.</returns>
    public static ScheduleConfiguration FromCreateOptions(ScheduleCreationOptions createOptions)
    {
        return new ScheduleConfiguration(createOptions.OrchestrationName, createOptions.ScheduleId)
        {
            OrchestrationInput = createOptions.OrchestrationInput,
            OrchestrationInstanceId = createOptions.OrchestrationInstanceId,
            StartAt = createOptions.StartAt,
            EndAt = createOptions.EndAt,
            Interval = createOptions.Interval,
            StartImmediatelyIfLate = createOptions.StartImmediatelyIfLate,
        };
    }

    /// <summary>
    /// Updates this configuration with the provided update options.
    /// </summary>
    /// <param name="updateOptions">The options to update the configuration with.</param>
    /// <returns>A set of field names that were updated.</returns>
    public HashSet<string> Update(ScheduleUpdateOptions updateOptions)
    {
        Check.NotNull(updateOptions, nameof(updateOptions));
        HashSet<string> updatedFields = new HashSet<string>();

        if (!string.IsNullOrEmpty(updateOptions.OrchestrationName))
        {
            this.OrchestrationName = updateOptions.OrchestrationName;
            updatedFields.Add(nameof(this.OrchestrationName));
        }

        if (updateOptions.OrchestrationInput == null)
        {
            this.OrchestrationInput = updateOptions.OrchestrationInput;
            updatedFields.Add(nameof(this.OrchestrationInput));
        }

        if (!string.IsNullOrEmpty(updateOptions.OrchestrationInstanceId))
        {
            this.OrchestrationInstanceId = updateOptions.OrchestrationInstanceId;
            updatedFields.Add(nameof(this.OrchestrationInstanceId));
        }

        if (updateOptions.StartAt.HasValue)
        {
            this.StartAt = updateOptions.StartAt;
            updatedFields.Add(nameof(this.StartAt));
        }

        if (updateOptions.EndAt.HasValue)
        {
            this.EndAt = updateOptions.EndAt;
            updatedFields.Add(nameof(this.EndAt));
        }

        if (updateOptions.Interval.HasValue)
        {
            this.Interval = updateOptions.Interval;
            updatedFields.Add(nameof(this.Interval));
        }

        if (updateOptions.StartImmediatelyIfLate.HasValue)
        {
            this.StartImmediatelyIfLate = updateOptions.StartImmediatelyIfLate.Value;
            updatedFields.Add(nameof(this.StartImmediatelyIfLate));
        }

        return updatedFields;
    }
}
