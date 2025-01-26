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

    internal ScheduleConfiguration? ScheduleConfiguration { get; set; }

    public void UpdateConfig(ScheduleConfiguration scheduleUpdateConfig)
    {
        Check.NotNull(this.ScheduleConfiguration, nameof(this.ScheduleConfiguration));
        Check.NotNull(scheduleUpdateConfig, nameof(scheduleUpdateConfig));

        this.ScheduleConfiguration.Version++;

        if (!string.IsNullOrEmpty(scheduleUpdateConfig.OrchestrationName))
        {
            this.ScheduleConfiguration.OrchestrationName = scheduleUpdateConfig.OrchestrationName;
        }

        if (!string.IsNullOrEmpty(scheduleUpdateConfig.ScheduleId))
        {
            this.ScheduleConfiguration.ScheduleId = scheduleUpdateConfig.ScheduleId;
        }

        if (scheduleUpdateConfig.OrchestrationInput == null)
        {
            this.ScheduleConfiguration.OrchestrationInput = scheduleUpdateConfig.OrchestrationInput;
        }

        if (scheduleUpdateConfig.StartAt.HasValue)
        {
            this.ScheduleConfiguration.StartAt = scheduleUpdateConfig.StartAt;
        }

        if (scheduleUpdateConfig.EndAt.HasValue)
        {
            this.ScheduleConfiguration.EndAt = scheduleUpdateConfig.EndAt;
        }

        if (scheduleUpdateConfig.Interval.HasValue)
        {
            this.ScheduleConfiguration.Interval = scheduleUpdateConfig.Interval;
        }

        if (!string.IsNullOrEmpty(scheduleUpdateConfig.CronExpression))
        {
            this.ScheduleConfiguration.CronExpression = scheduleUpdateConfig.CronExpression;
        }

        if (scheduleUpdateConfig.MaxOccurrence != 0)
        {
            this.ScheduleConfiguration.MaxOccurrence = scheduleUpdateConfig.MaxOccurrence;
        }

        // Only update if the customer explicitly set a value
        if (scheduleUpdateConfig.StartImmediatelyIfLate.HasValue)
        {
            this.ScheduleConfiguration.StartImmediatelyIfLate = scheduleUpdateConfig.StartImmediatelyIfLate.Value;
        }
    }

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

    public DateTimeOffset? StartAt { get; set; }

    public DateTimeOffset? EndAt { get; set; }

    public TimeSpan? Interval { get; set; }

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
    Failed,       // Schedule has failed
}

class Schedule : TaskEntity<ScheduleState>
{
    readonly ILogger<Schedule> logger;

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
        this.State.Status = ScheduleStatus.Active;

        // Run schedule after creation
        context.SignalEntity(new EntityInstanceId(nameof(Schedule), this.State.ScheduleConfiguration.ScheduleId), "RunSchedule", this.State.ExecutionToken);
    }

    /// <summary>
    /// Updates an existing schedule.
    /// </summary>
    public void UpdateSchedule(TaskEntityContext context, ScheduleConfiguration scheduleUpdateConfig)
    {
        Verify.NotNull(scheduleUpdateConfig, nameof(scheduleUpdateConfig));
        Verify.NotNull(this.State.ScheduleConfiguration, nameof(this.State.ScheduleConfiguration));

        this.logger.LogInformation($"Updating schedule with details: {scheduleUpdateConfig}");

        this.State.UpdateConfig(scheduleUpdateConfig);
        this.State.RefreshScheduleRunExecutionToken();

        // Run schedule after update
        context.SignalEntity(new EntityInstanceId(nameof(Schedule), this.State.ScheduleConfiguration.ScheduleId), "RunSchedule", this.State.ExecutionToken);
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
        this.State.Status = ScheduleStatus.Paused;
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

        this.State.Status = ScheduleStatus.Active;
        this.logger.LogInformation("Schedule resumed.");

        context.SignalEntity(new EntityInstanceId(nameof(Schedule), this.State.ScheduleConfiguration.ScheduleId), "RunSchedule", this.State.ExecutionToken);
    }

    // TODO: Only implement this there is any cleanup shall be performed within entity before purging the instance.
    public void DeleteSchedule()
    {
        throw new NotImplementedException();
    }

    public void RunSchedule(TaskEntityContext context, string executionToken)
    {
        if (executionToken != this.State.ExecutionToken)
        {
            // Execution token has expired, log and return
            this.logger.LogInformation(
                "Skipping schedule run - execution token {token} has expired",
                executionToken);
            return;
        }

        if (this.State.Status != ScheduleStatus.Active)
        {
            throw new InvalidOperationException("Schedule must be in Active status to run.");
        }

        // TODO: Implement all schedule config properties
        // if startat is null, then start immediately
        // first check startat, compute gap with current time, if gap is negative, then start immediately
        // if gap is positive, then wait for gap seconds and then signal runschedule with delay of gap time
        // first check if there is already existing orchestration instance with same orchestration name
        // if there is no existing orchestration instance, then create a new one
        // if there is existing orchestration instance, then check if it is done, if it is done, then create a new one
        // if there is existing orchestration instance, then check if it is not done, then skip
    }
}
