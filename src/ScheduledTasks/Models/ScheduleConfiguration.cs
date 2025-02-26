// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Configuration for a scheduled task.
/// </summary>
class ScheduleConfiguration
{
    string orchestrationName;
    TimeSpan interval;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleConfiguration"/> class.
    /// </summary>
    /// <param name="scheduleId">The ID of the schedule.</param>
    /// <param name="orchestrationName">The name of the orchestration to schedule.</param>
    /// <param name="interval">The interval between schedule executions. Must be positive and at least 1 second.</param>
    public ScheduleConfiguration(string scheduleId, string orchestrationName, TimeSpan interval)
    {
        this.ScheduleId = Check.NotNullOrEmpty(scheduleId, nameof(scheduleId));
        this.orchestrationName = Check.NotNullOrEmpty(orchestrationName, nameof(orchestrationName));
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
    /// Gets or Sets the name of the orchestration function to schedule.
    /// </summary>
    public string OrchestrationName
    {
        get => this.orchestrationName;
        set => this.orchestrationName = Check.NotNullOrEmpty(value, nameof(this.OrchestrationName));
    }

    /// <summary>
    /// Gets the ID of the schedule.
    /// </summary>
    public string ScheduleId { get; }

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
    public TimeSpan Interval
    {
        get => this.interval;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentException("Interval must be positive", nameof(value));
            }

            if (value.TotalSeconds < 1)
            {
                throw new ArgumentException("Interval must be at least 1 second", nameof(value));
            }

            this.interval = value;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether to start the orchestration immediately when the current time is past the StartAt time.
    /// By default it is false.
    /// If false, the first run will be scheduled at the next interval based on the original start time.
    /// If true, the first run will start immediately and subsequent runs will follow the regular interval.
    /// </summary>
    public bool StartImmediatelyIfLate { get; set; }

    /// <summary>
    /// Creates a new configuration from the provided creation options.
    /// </summary>
    /// <param name="createOptions">The options to create the configuration from.</param>
    /// <returns>A new schedule configuration.</returns>
    public static ScheduleConfiguration FromCreateOptions(ScheduleCreationOptions createOptions)
    {
        Check.NotNull(createOptions, nameof(createOptions));

        ScheduleConfiguration scheduleConfig = new ScheduleConfiguration(createOptions.ScheduleId, createOptions.OrchestrationName, createOptions.Interval)
        {
            OrchestrationInput = createOptions.OrchestrationInput,
            OrchestrationInstanceId = createOptions.OrchestrationInstanceId,
            StartAt = createOptions.StartAt,
            EndAt = createOptions.EndAt,
            StartImmediatelyIfLate = createOptions.StartImmediatelyIfLate,
        };

        scheduleConfig.Validate();

        return scheduleConfig;
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

        if (!string.IsNullOrEmpty(updateOptions.OrchestrationName)
            && updateOptions.OrchestrationName != this.OrchestrationName)
        {
            this.OrchestrationName = updateOptions.OrchestrationName;
            updatedFields.Add(nameof(this.OrchestrationName));
        }

        if (!string.IsNullOrEmpty(updateOptions.OrchestrationInput)
            && updateOptions.OrchestrationInput != this.OrchestrationInput)
        {
            this.OrchestrationInput = updateOptions.OrchestrationInput;
            updatedFields.Add(nameof(this.OrchestrationInput));
        }

        if (!string.IsNullOrEmpty(updateOptions.OrchestrationInstanceId)
            && updateOptions.OrchestrationInstanceId != this.OrchestrationInstanceId)
        {
            this.OrchestrationInstanceId = updateOptions.OrchestrationInstanceId;
            updatedFields.Add(nameof(this.OrchestrationInstanceId));
        }

        if (updateOptions.StartAt.HasValue
            && updateOptions.StartAt != this.StartAt)
        {
            this.StartAt = updateOptions.StartAt;
            updatedFields.Add(nameof(this.StartAt));
        }

        if (updateOptions.EndAt.HasValue
            && updateOptions.EndAt != this.EndAt)
        {
            this.EndAt = updateOptions.EndAt;
            updatedFields.Add(nameof(this.EndAt));
        }

        if (updateOptions.Interval.HasValue
            && updateOptions.Interval != this.Interval)
        {
            this.Interval = updateOptions.Interval.Value;
            updatedFields.Add(nameof(this.Interval));
        }

        if (updateOptions.StartImmediatelyIfLate.HasValue
            && updateOptions.StartImmediatelyIfLate != this.StartImmediatelyIfLate)
        {
            this.StartImmediatelyIfLate = updateOptions.StartImmediatelyIfLate.Value;
            updatedFields.Add(nameof(this.StartImmediatelyIfLate));
        }

        this.Validate();
        return updatedFields;
    }

    [MemberNotNull(nameof(StartAt), nameof(EndAt))]
    void Validate()
    {
        if (this.StartAt.HasValue && this.EndAt.HasValue && this.StartAt.Value > this.EndAt.Value)
        {
            throw new ArgumentException("StartAt cannot be later than EndAt.", nameof(this.StartAt));
        }
    }
}
