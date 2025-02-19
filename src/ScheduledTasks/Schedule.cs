// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.ScheduledTasks;

// TODO: logging

/// <summary>
/// Entity that manages the state and execution of a scheduled task.
/// </summary>
/// <remarks>
/// The Schedule entity maintains the configuration and state of a scheduled task,
/// handling operations like creation, updates, pausing/resuming, and executing the task
/// according to the defined schedule.
/// </remarks>
/// <param name="logger">Logger for recording schedule operations and events.</param>
class Schedule(ILogger<Schedule> logger) : TaskEntity<ScheduleState>
{
    readonly ILogger<Schedule> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Creates a new schedule with the specified configuration.
    /// </summary>
    /// <param name="context">The task entity context.</param>
    /// <param name="scheduleConfigurationCreateOptions">The configuration options for creating the schedule.</param>
    /// <exception cref="ArgumentNullException">Thrown when scheduleConfigurationCreateOptions is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the schedule is already created.</exception>
    public void CreateSchedule(TaskEntityContext context, ScheduleCreationOptions scheduleConfigurationCreateOptions)
    {
        Verify.NotNull(scheduleConfigurationCreateOptions, nameof(scheduleConfigurationCreateOptions));

        if (this.State.Status != ScheduleStatus.Uninitialized)
        {
            throw new InvalidOperationException("Schedule is already created.");
        }

        this.State.ScheduleConfiguration = ScheduleConfiguration.FromCreateOptions(scheduleConfigurationCreateOptions);
        this.TryStatusTransition(ScheduleStatus.Active);

        // Signal to run schedule immediately after creation and let runSchedule determine if it should run immediately
        // or later to separate response from schedule creation and schedule responsibilities
        context.SignalEntity(new EntityInstanceId(nameof(Schedule), this.State.ScheduleConfiguration.ScheduleId), nameof(this.RunSchedule), this.State.ExecutionToken);
    }

    /// <summary>
    /// Updates an existing schedule.
    /// </summary>
    /// <param name="context">The task entity context.</param>
    /// <param name="scheduleUpdateOptions">The options for updating the schedule configuration.</param>
    /// <exception cref="ArgumentNullException">Thrown when scheduleConfigUpdateOptions is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the schedule is not created.</exception>
    public void UpdateSchedule(TaskEntityContext context, ScheduleUpdateOptions scheduleUpdateOptions)
    {
        Verify.NotNull(scheduleUpdateOptions, nameof(scheduleUpdateOptions));
        Verify.NotNull(this.State.ScheduleConfiguration, nameof(this.State.ScheduleConfiguration));

        this.logger.UpdatingSchedule(this.State.ScheduleConfiguration.ScheduleId, scheduleUpdateOptions);

        HashSet<string> updatedScheduleConfigFields = this.State.UpdateConfig(scheduleUpdateOptions);
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
    /// <param name="context">The task entity context.</param>
    /// <exception cref="InvalidOperationException">Thrown when the schedule is not paused.</exception>
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

    // TODO: Verify use built int entity delete operation to delete schedule

    // TODO: Support other schedule option properties like cron expression, max occurrence, etc.

    /// <summary>
    /// Runs the schedule based on the defined configuration.
    /// </summary>
    /// <param name="context">The task entity context.</param>
    /// <param name="executionToken">The execution token for the schedule.</param>
    /// <exception cref="InvalidOperationException">Thrown when the schedule is not active or interval is not specified.</exception>
    public void RunSchedule(TaskEntityContext context, string executionToken)
    {
        Verify.NotNull(this.State.ScheduleConfiguration, nameof(this.State.ScheduleConfiguration));
        if (this.State.ScheduleConfiguration.Interval == null)
        {
            throw new InvalidOperationException("Schedule interval must be specified.");
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
            this.State.ExecutionToken,
            new SignalEntityOptions { SignalTime = this.State.NextRunAt.Value });
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

        if (!ScheduleTransitions.TryGetValidTransitions(from, out validTargetStates) || !validTargetStates.Contains(to))
        {
            throw new InvalidOperationException($"Invalid state transition: Cannot transition from {from} to {to}");
        }

        this.State.Status = to;
    }
}
