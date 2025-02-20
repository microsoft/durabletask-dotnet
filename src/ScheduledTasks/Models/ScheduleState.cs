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
    /// <param name="scheduleUpdateOptions">The update options to apply.</param>
    /// <returns>A set of field names that were updated.</returns>
    public HashSet<string> UpdateConfig(ScheduleUpdateOptions scheduleUpdateOptions)
    {
        Check.NotNull(this.ScheduleConfiguration, nameof(this.ScheduleConfiguration));
        Check.NotNull(scheduleUpdateOptions, nameof(scheduleUpdateOptions));

        HashSet<string> updatedFields = new HashSet<string>();

        if (!string.IsNullOrEmpty(scheduleUpdateOptions.OrchestrationName))
        {
            this.ScheduleConfiguration.OrchestrationName = scheduleUpdateOptions.OrchestrationName;
            updatedFields.Add(nameof(this.ScheduleConfiguration.OrchestrationName));
        }

        if (scheduleUpdateOptions.OrchestrationInput == null)
        {
            this.ScheduleConfiguration.OrchestrationInput = scheduleUpdateOptions.OrchestrationInput;
            updatedFields.Add(nameof(this.ScheduleConfiguration.OrchestrationInput));
        }

        if (scheduleUpdateOptions.StartAt.HasValue)
        {
            this.ScheduleConfiguration.StartAt = scheduleUpdateOptions.StartAt;
            updatedFields.Add(nameof(this.ScheduleConfiguration.StartAt));
        }

        if (scheduleUpdateOptions.EndAt.HasValue)
        {
            this.ScheduleConfiguration.EndAt = scheduleUpdateOptions.EndAt;
            updatedFields.Add(nameof(this.ScheduleConfiguration.EndAt));
        }

        if (scheduleUpdateOptions.Interval.HasValue)
        {
            this.ScheduleConfiguration.Interval = scheduleUpdateOptions.Interval;
            updatedFields.Add(nameof(this.ScheduleConfiguration.Interval));
        }

        if (!string.IsNullOrEmpty(scheduleUpdateOptions.CronExpression))
        {
            this.ScheduleConfiguration.CronExpression = scheduleUpdateOptions.CronExpression;
            updatedFields.Add(nameof(this.ScheduleConfiguration.CronExpression));
        }

        if (scheduleUpdateOptions.MaxOccurrence != 0)
        {
            this.ScheduleConfiguration.MaxOccurrence = scheduleUpdateOptions.MaxOccurrence;
            updatedFields.Add(nameof(this.ScheduleConfiguration.MaxOccurrence));
        }

        // Only update if the customer explicitly set a value
        if (scheduleUpdateOptions.StartImmediatelyIfLate.HasValue)
        {
            this.ScheduleConfiguration.StartImmediatelyIfLate = scheduleUpdateOptions.StartImmediatelyIfLate.Value;
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
