// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Represents the current state of a schedule.
/// </summary>
class ScheduleState
{
    /// <summary>
    /// Gets or sets the current status of the schedule.
    /// </summary>
    internal ScheduleStatus Status { get; set; } = ScheduleStatus.Uninitialized;

    /// <summary>
    /// Gets or sets the execution token used to validate schedule operations.
    /// </summary>
    internal string ExecutionToken { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the last time the schedule was run.
    /// </summary>
    internal DateTimeOffset? LastRunAt { get; set; }

    /// <summary>
    /// Gets or sets the next scheduled run time.
    /// </summary>
    internal DateTimeOffset? NextRunAt { get; set; }

    /// <summary>
    /// Gets or sets the schedule configuration.
    /// </summary>
    internal ScheduleConfiguration? ScheduleConfiguration { get; set; }

    /// <summary>
    /// Updates the schedule configuration with the provided options.
    /// </summary>
    /// <param name="scheduleConfigUpdateOptions">The update options to apply.</param>
    /// <returns>A set of field names that were updated.</returns>
    public HashSet<string> UpdateConfig(ScheduleUpdateOptions scheduleConfigUpdateOptions)
    {
        Check.NotNull(this.ScheduleConfiguration, nameof(this.ScheduleConfiguration));
        Check.NotNull(scheduleConfigUpdateOptions, nameof(scheduleConfigUpdateOptions));

        HashSet<string> updatedFields = new HashSet<string>();

        if (!string.IsNullOrEmpty(scheduleConfigUpdateOptions.OrchestrationName))
        {
            this.ScheduleConfiguration.OrchestrationName = scheduleConfigUpdateOptions.OrchestrationName;
            updatedFields.Add(nameof(this.ScheduleConfiguration.OrchestrationName));
        }

        if (scheduleConfigUpdateOptions.OrchestrationInput == null)
        {
            this.ScheduleConfiguration.OrchestrationInput = scheduleConfigUpdateOptions.OrchestrationInput;
            updatedFields.Add(nameof(this.ScheduleConfiguration.OrchestrationInput));
        }

        if (scheduleConfigUpdateOptions.StartAt.HasValue)
        {
            this.ScheduleConfiguration.StartAt = scheduleConfigUpdateOptions.StartAt;
            updatedFields.Add(nameof(this.ScheduleConfiguration.StartAt));
        }

        if (scheduleConfigUpdateOptions.EndAt.HasValue)
        {
            this.ScheduleConfiguration.EndAt = scheduleConfigUpdateOptions.EndAt;
            updatedFields.Add(nameof(this.ScheduleConfiguration.EndAt));
        }

        if (scheduleConfigUpdateOptions.Interval.HasValue)
        {
            this.ScheduleConfiguration.Interval = scheduleConfigUpdateOptions.Interval;
            updatedFields.Add(nameof(this.ScheduleConfiguration.Interval));
        }

        if (!string.IsNullOrEmpty(scheduleConfigUpdateOptions.CronExpression))
        {
            this.ScheduleConfiguration.CronExpression = scheduleConfigUpdateOptions.CronExpression;
            updatedFields.Add(nameof(this.ScheduleConfiguration.CronExpression));
        }

        if (scheduleConfigUpdateOptions.MaxOccurrence != 0)
        {
            this.ScheduleConfiguration.MaxOccurrence = scheduleConfigUpdateOptions.MaxOccurrence;
            updatedFields.Add(nameof(this.ScheduleConfiguration.MaxOccurrence));
        }

        // Only update if the customer explicitly set a value
        if (scheduleConfigUpdateOptions.StartImmediatelyIfLate.HasValue)
        {
            this.ScheduleConfiguration.StartImmediatelyIfLate = scheduleConfigUpdateOptions.StartImmediatelyIfLate.Value;
            updatedFields.Add(nameof(this.ScheduleConfiguration.StartImmediatelyIfLate));
        }

        return updatedFields;
    }

    /// <summary>
    /// Refreshes the execution token to invalidate pending schedule operations.
    /// </summary>
    public void RefreshScheduleRunExecutionToken()
    {
        this.ExecutionToken = Guid.NewGuid().ToString("N");
    }
}
