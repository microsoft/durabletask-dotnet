// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace DurableTask.Abstractions.Entities.Schedule;

class ScheduleState
{
    internal ScheduleStatus Status { get; set; } = ScheduleStatus.Uninitialized;

    internal string ExecutionToken { get; set; } = Guid.NewGuid().ToString("N");

    internal DateTimeOffset? LastRunAt { get; set; }

    internal DateTimeOffset? NextRunAt { get; set; }

    internal ScheduleConfiguration? ScheduleConfiguration { get; set; }

    public HashSet<string> UpdateConfig(ScheduleConfiguration scheduleUpdateConfig)
    {
        Check.NotNull(this.ScheduleConfiguration, nameof(this.ScheduleConfiguration));
        Check.NotNull(scheduleUpdateConfig, nameof(scheduleUpdateConfig));

        HashSet<string> updatedFields = new HashSet<string>();

        this.ScheduleConfiguration.Version++;

        if (!string.IsNullOrEmpty(scheduleUpdateConfig.OrchestrationName))
        {
            this.ScheduleConfiguration.OrchestrationName = scheduleUpdateConfig.OrchestrationName;
            updatedFields.Add(nameof(this.ScheduleConfiguration.OrchestrationName));
        }

        if (!string.IsNullOrEmpty(scheduleUpdateConfig.ScheduleId))
        {
            this.ScheduleConfiguration.ScheduleId = scheduleUpdateConfig.ScheduleId;
            updatedFields.Add(nameof(this.ScheduleConfiguration.ScheduleId));
        }

        if (scheduleUpdateConfig.OrchestrationInput == null)
        {
            this.ScheduleConfiguration.OrchestrationInput = scheduleUpdateConfig.OrchestrationInput;
            updatedFields.Add(nameof(this.ScheduleConfiguration.OrchestrationInput));
        }

        if (scheduleUpdateConfig.StartAt.HasValue)
        {
            this.ScheduleConfiguration.StartAt = scheduleUpdateConfig.StartAt;
            updatedFields.Add(nameof(this.ScheduleConfiguration.StartAt));
        }

        if (scheduleUpdateConfig.EndAt.HasValue)
        {
            this.ScheduleConfiguration.EndAt = scheduleUpdateConfig.EndAt;
            updatedFields.Add(nameof(this.ScheduleConfiguration.EndAt));
        }

        if (scheduleUpdateConfig.Interval.HasValue)
        {
            this.ScheduleConfiguration.Interval = scheduleUpdateConfig.Interval;
            updatedFields.Add(nameof(this.ScheduleConfiguration.Interval));
        }

        if (!string.IsNullOrEmpty(scheduleUpdateConfig.CronExpression))
        {
            this.ScheduleConfiguration.CronExpression = scheduleUpdateConfig.CronExpression;
            updatedFields.Add(nameof(this.ScheduleConfiguration.CronExpression));
        }

        if (scheduleUpdateConfig.MaxOccurrence != 0)
        {
            this.ScheduleConfiguration.MaxOccurrence = scheduleUpdateConfig.MaxOccurrence;
            updatedFields.Add(nameof(this.ScheduleConfiguration.MaxOccurrence));
        }

        // Only update if the customer explicitly set a value
        if (scheduleUpdateConfig.StartImmediatelyIfLate.HasValue)
        {
            this.ScheduleConfiguration.StartImmediatelyIfLate = scheduleUpdateConfig.StartImmediatelyIfLate.Value;
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

class ScheduleConfiguration
{
    public ScheduleConfiguration(string orchestrationName, string scheduleId)
    {
        this.orchestrationName = Check.NotNullOrEmpty(orchestrationName, nameof(orchestrationName));
        this.ScheduleId = scheduleId ?? Guid.NewGuid().ToString("N");
        this.Version++;
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

    string scheduleId;

    public string ScheduleId
    {
        get => this.scheduleId;
        set
        {
            this.scheduleId = Check.NotNullOrEmpty(value, nameof(value));
        }
    }

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

    internal int Version { get; set; } // Tracking schedule config version
}

enum ScheduleStatus
{
    Uninitialized, // Schedule has not been created
    Active,       // Schedule is active and running
    Paused,       // Schedule is paused
}

class Schedule : TaskEntity<ScheduleState>
{
    readonly ILogger<Schedule> logger;

    // Valid schedule status transitions
    readonly Dictionary<ScheduleStatus, HashSet<ScheduleStatus>> validTransitions = new Dictionary<ScheduleStatus, HashSet<ScheduleStatus>>
    {
        {
            ScheduleStatus.Uninitialized,
            new HashSet<ScheduleStatus> { ScheduleStatus.Active }
        },
        {
            ScheduleStatus.Active,
            new HashSet<ScheduleStatus> { ScheduleStatus.Paused }
        },
        {
            ScheduleStatus.Paused,
            new HashSet<ScheduleStatus> { ScheduleStatus.Active }
        },
    };

    public Schedule(ILogger<Schedule> logger)
    {
        this.logger = logger;
    }

    public void CreateSchedule(TaskEntityContext context, ScheduleConfiguration scheduleCreationConfig)
    {
        Verify.NotNull(scheduleCreationConfig, nameof(scheduleCreationConfig));

        if (this.State.Status != ScheduleStatus.Uninitialized)
        {
            throw new InvalidOperationException("Schedule is already created.");
        }

        this.logger.LogInformation($"Creating schedule with options: {scheduleCreationConfig}");

        this.State.ScheduleConfiguration = scheduleCreationConfig;
        this.TryStatusTransition(ScheduleStatus.Active);

        // Signal to run schedule immediately after creation and let runSchedule determine if it should run immediately
        // or later to separate response from schedule creation and schedule responsibilities
        context.SignalEntity(new EntityInstanceId(nameof(Schedule), this.State.ScheduleConfiguration.ScheduleId), nameof(this.RunSchedule), this.State.ExecutionToken);
    }

    /// <summary>
    /// Updates an existing schedule.
    /// </summary>
    public void UpdateSchedule(TaskEntityContext context, ScheduleConfiguration scheduleUpdateConfig)
    {
        Verify.NotNull(scheduleUpdateConfig, nameof(scheduleUpdateConfig));
        Verify.NotNull(this.State.ScheduleConfiguration, nameof(this.State.ScheduleConfiguration));

        this.logger.LogInformation($"Updating schedule with details: {scheduleUpdateConfig}");

        HashSet<string> updatedScheduleConfigFields = this.State.UpdateConfig(scheduleUpdateConfig);
        if (updatedScheduleConfigFields.Count == 0)
        {
            // no need to interrupt and update current schedule run as there is no change in the schedule config
            this.logger.LogInformation("Schedule configuration is up to date.");
            return;
        }

        // after schedule config is updated, perform post-config-update logic separately
        foreach (string updatedScheduleConfigField in updatedScheduleConfigFields)
        {
            switch (updatedScheduleConfigField)
            {
                case nameof(this.State.ScheduleConfiguration.StartAt):
                case nameof(this.State.ScheduleConfiguration.Interval):
                    this.State.NextRunAt = null;
                    break;

                // TODO: add other fields's callback logic after config update if any
                default:
                    break;
            }
        }

        this.State.RefreshScheduleRunExecutionToken();

        // Signal to run schedule immediately after update and let runSchedule determine if it should run immediately
        // or later to separate response from schedule creation and schedule responsibilities
        context.SignalEntity(new EntityInstanceId(nameof(Schedule), this.State.ScheduleConfiguration.ScheduleId), nameof(this.RunSchedule), this.State.ExecutionToken);
    }

    /// <summary>
    /// Pauses the schedule.
    /// </summary>
    public void PauseSchedule()
    {
        if (this.State.Status != ScheduleStatus.Active)
        {
            throw new InvalidOperationException("Schedule must be in Active status to pause.");
        }

        // Transition to Paused state
        this.TryStatusTransition(ScheduleStatus.Paused);
        this.State.NextRunAt = null;
        this.State.RefreshScheduleRunExecutionToken();

        this.logger.LogInformation("Schedule paused.");
    }

    /// <summary>
    /// Resumes the schedule.
    /// </summary>
    public void ResumeSchedule(TaskEntityContext context)
    {
        Verify.NotNull(this.State.ScheduleConfiguration, nameof(this.State.ScheduleConfiguration));
        if (this.State.Status != ScheduleStatus.Paused)
        {
            throw new InvalidOperationException("Schedule must be in Paused state to resume.");
        }

        this.TryStatusTransition(ScheduleStatus.Active);
        this.State.NextRunAt = null;
        this.logger.LogInformation("Schedule resumed.");

        // compute next run based on startat and interval
        context.SignalEntity(new EntityInstanceId(nameof(Schedule), this.State.ScheduleConfiguration.ScheduleId), nameof(this.RunSchedule), this.State.ExecutionToken);
    }

    // TODO: Only implement this there is any cleanup shall be performed within entity before purging the instance.
    public void DeleteSchedule()
    {
        throw new NotImplementedException();
    }

    // TODO: Support other schedule option properties like cron expression, max occurrence, etc.
    public void RunSchedule(TaskEntityContext context, string executionToken)
    {
        Verify.NotNull(this.State.ScheduleConfiguration, nameof(this.State.ScheduleConfiguration));
        if (this.State.ScheduleConfiguration.Interval == null)
        {
            throw new ArgumentNullException(nameof(this.State.ScheduleConfiguration.Interval));
        }

        if (executionToken != this.State.ExecutionToken)
        {
            this.logger.LogInformation("Cancel schedule run - execution token {token} has expired", executionToken);
            return;
        }

        if (this.State.Status != ScheduleStatus.Active)
        {
            throw new InvalidOperationException("Schedule must be in Active status to run.");
        }

        // run schedule based on next run at
        // need to enforce the constraint here NextRunAt truly represents the next run at
        // if next run at is null, this means schedule is changed, we compute the next run at based on startat and update
        // else if next run at is set, then we run at next run at
        if (!this.State.NextRunAt.HasValue)
        {
            // check whats last run at time, if not set, meaning it has not run once, we run at startat
            // else, it has run before, we cant run at startat, need to compute next run at based on last run at + num of intervals between last runtime and now plus 1
            if (!this.State.LastRunAt.HasValue)
            {
                this.State.NextRunAt = this.State.ScheduleConfiguration.StartAt;
            }
            else
            {
                // Calculate number of intervals between last run and now
                TimeSpan timeSinceLastRun = DateTimeOffset.UtcNow - this.State.LastRunAt.Value;
                int intervalsElapsed = (int)(timeSinceLastRun.Ticks / this.State.ScheduleConfiguration.Interval.Value.Ticks);

                // Compute the next run time
                this.State.NextRunAt = this.State.LastRunAt.Value + TimeSpan.FromTicks(this.State.ScheduleConfiguration.Interval.Value.Ticks * (intervalsElapsed + 1));
            }
        }

        DateTimeOffset currentTime = DateTimeOffset.UtcNow;

        if (!this.State.NextRunAt.HasValue || this.State.NextRunAt!.Value <= currentTime)
        {
            this.State.NextRunAt = currentTime;
            this.StartOrchestrationIfNotRunning(context);
            this.State.LastRunAt = this.State.NextRunAt;
            this.State.NextRunAt = this.State.LastRunAt.Value + this.State.ScheduleConfiguration.Interval.Value;
        }

        context.SignalEntity(
            new EntityInstanceId(
                nameof(Schedule),
                this.State.ScheduleConfiguration.ScheduleId),
            nameof(this.RunSchedule),
            this.State.ExecutionToken, new SignalEntityOptions { SignalTime = this.State.NextRunAt.Value });
    }

    void StartOrchestrationIfNotRunning(TaskEntityContext context)
    {
        ScheduleConfiguration? config = this.State.ScheduleConfiguration;
        context.ScheduleNewOrchestration(new TaskName(config!.OrchestrationName), config!.OrchestrationInput, new StartOrchestrationOptions(config!.OrchestrationInstanceId));
    }

    void TryStatusTransition(ScheduleStatus to)
    {
        // Check if transition is valid
        HashSet<ScheduleStatus> validTargetStates;
        ScheduleStatus from = this.State.Status;

        if (!this.validTransitions.TryGetValue(from, out validTargetStates) || !validTargetStates.Contains(to))
        {
            throw new InvalidOperationException($"Invalid state transition: Cannot transition from {from} to {to}");
        }

        this.State.Status = to;
    }
}
