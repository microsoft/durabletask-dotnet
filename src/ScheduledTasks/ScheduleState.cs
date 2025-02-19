// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

class ScheduleState
{
    internal ScheduleStatus Status { get; set; } = ScheduleStatus.Uninitialized;

    internal string ExecutionToken { get; set; } = Guid.NewGuid().ToString("N");

    internal DateTimeOffset? LastRunAt { get; set; }

    internal DateTimeOffset? NextRunAt { get; set; }

    internal ScheduleConfiguration? ScheduleConfiguration { get; set; }

    public HashSet<string> UpdateConfig(ScheduleConfigurationUpdateOptions scheduleConfigUpdateOptions)
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

    // To stop potential runSchedule operation scheduled after the schedule update/pause, invalidate the execution token and let it exit gracefully
    // This could incur little overhead as ideally the runSchedule with old token should be killed immediately
    // but there is no support to cancel pending entity operations currently, can be a todo item
    public void RefreshScheduleRunExecutionToken()
    {
        this.ExecutionToken = Guid.NewGuid().ToString("N");
    }
}
