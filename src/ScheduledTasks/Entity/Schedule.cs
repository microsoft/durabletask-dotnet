// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.ScheduledTasks;

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
    /// <param name="scheduleCreationOptions">The configuration options for creating the schedule.</param>
    /// <exception cref="ArgumentNullException">Thrown when scheduleConfigurationCreateOptions is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the schedule is already created.</exception>
    public void CreateSchedule(TaskEntityContext context, ScheduleCreationOptions scheduleCreationOptions)
    {
        try
        {
            if (!this.CanTransitionTo(nameof(this.CreateSchedule), ScheduleStatus.Active))
            {
                throw new ScheduleInvalidTransitionException(scheduleCreationOptions?.ScheduleId ?? string.Empty, this.State.Status, ScheduleStatus.Active, nameof(this.CreateSchedule));
            }

            if (scheduleCreationOptions == null)
            {
                throw new ScheduleClientValidationException(string.Empty, "Schedule creation options cannot be null");
            }

            this.State.ScheduleConfiguration = ScheduleConfiguration.FromCreateOptions(scheduleCreationOptions);
            this.TryStatusTransition(nameof(this.CreateSchedule), ScheduleStatus.Active);

            this.logger.CreatedSchedule(this.State.ScheduleConfiguration.ScheduleId);

            // Signal to run schedule immediately after creation and let runSchedule determine if it should run immediately
            // or later to separate response from schedule creation and schedule responsibilities
            context.SignalEntity(new EntityInstanceId(nameof(Schedule), this.State.ScheduleConfiguration.ScheduleId), nameof(this.RunSchedule), this.State.ExecutionToken);
        }
        catch (Exception ex)
        {
            this.logger.ScheduleOperationError(scheduleCreationOptions.ScheduleId, nameof(this.CreateSchedule), "Failed to create schedule", ex);
            throw;
        }
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
        try
        {
            if (!this.CanTransitionTo(nameof(this.UpdateSchedule), this.State.Status))
            {
                throw new ScheduleInvalidTransitionException(this.State.ScheduleConfiguration?.ScheduleId ?? string.Empty, this.State.Status, this.State.Status, nameof(this.UpdateSchedule));
            }

            if (scheduleUpdateOptions == null)
            {
                throw new ScheduleClientValidationException(this.State.ScheduleConfiguration?.ScheduleId ?? string.Empty, "Schedule update options cannot be null");
            }

            Verify.NotNull(this.State.ScheduleConfiguration, nameof(this.State.ScheduleConfiguration));

            HashSet<string> updatedScheduleConfigFields = this.State.ScheduleConfiguration.Update(scheduleUpdateOptions);
            if (updatedScheduleConfigFields.Count == 0)
            {
                // no need to interrupt and update current schedule run as there is no change in the schedule config
                this.logger.ScheduleOperationDebug(this.State.ScheduleConfiguration.ScheduleId, nameof(this.UpdateSchedule), "Schedule configuration is up to date.");
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

            this.logger.UpdatedSchedule(this.State.ScheduleConfiguration.ScheduleId);

            // Signal to run schedule immediately after update and let runSchedule determine if it should run immediately
            // or later to separate response from schedule creation and schedule responsibilities
            context.SignalEntity(new EntityInstanceId(nameof(Schedule), this.State.ScheduleConfiguration.ScheduleId), nameof(this.RunSchedule), this.State.ExecutionToken);
        }
        catch (Exception ex)
        {
            this.logger.ScheduleOperationError(this.State.ScheduleConfiguration?.ScheduleId ?? string.Empty, nameof(this.UpdateSchedule), "Failed to update schedule", ex);
            throw;
        }
    }

    /// <summary>
    /// Pauses the schedule.
    /// </summary>
    /// <param name="context">The task entity context.</param>
    public void PauseSchedule(TaskEntityContext context)
    {
        try
        {
            if (!this.CanTransitionTo(nameof(this.PauseSchedule), ScheduleStatus.Paused))
            {
                throw new ScheduleInvalidTransitionException(this.State.ScheduleConfiguration?.ScheduleId ?? string.Empty, this.State.Status, ScheduleStatus.Paused, nameof(this.PauseSchedule));
            }

            Verify.NotNull(this.State.ScheduleConfiguration, nameof(this.State.ScheduleConfiguration));

            // Transition to Paused state
            this.TryStatusTransition(nameof(this.PauseSchedule), ScheduleStatus.Paused);
            this.State.NextRunAt = null;
            this.State.RefreshScheduleRunExecutionToken();

            this.logger.PausedSchedule(this.State.ScheduleConfiguration.ScheduleId);
        }
        catch (Exception ex)
        {
            this.logger.ScheduleOperationError(this.State.ScheduleConfiguration?.ScheduleId ?? string.Empty, nameof(this.PauseSchedule), "Failed to pause schedule", ex);
            throw;
        }
    }

    /// <summary>
    /// Resumes the schedule.
    /// </summary>
    /// <param name="context">The task entity context.</param>
    /// <exception cref="InvalidOperationException">Thrown when the schedule is not paused.</exception>
    public void ResumeSchedule(TaskEntityContext context)
    {
        try
        {
            if (!this.CanTransitionTo(nameof(this.ResumeSchedule), ScheduleStatus.Active))
            {
                throw new ScheduleInvalidTransitionException(this.State.ScheduleConfiguration?.ScheduleId ?? string.Empty, this.State.Status, ScheduleStatus.Active, nameof(this.ResumeSchedule));
            }

            Verify.NotNull(this.State.ScheduleConfiguration, nameof(this.State.ScheduleConfiguration));

            this.TryStatusTransition(nameof(this.ResumeSchedule), ScheduleStatus.Active);
            this.State.NextRunAt = null;
            this.logger.ResumedSchedule(this.State.ScheduleConfiguration.ScheduleId);

            // compute next run based on startat and interval
            context.SignalEntity(new EntityInstanceId(nameof(Schedule), this.State.ScheduleConfiguration.ScheduleId), nameof(this.RunSchedule), this.State.ExecutionToken);
        }
        catch (Exception ex)
        {
            this.logger.ScheduleOperationError(this.State.ScheduleConfiguration?.ScheduleId ?? string.Empty, nameof(this.ResumeSchedule), "Failed to resume schedule", ex);
            throw;
        }
    }

    /// <summary>
    /// Runs the schedule based on the defined configuration.
    /// </summary>
    /// <param name="context">The task entity context.</param>
    /// <param name="executionToken">The execution token for the schedule.</param>
    /// <exception cref="InvalidOperationException">Thrown when the schedule is not active or interval is not specified.</exception>
    public void RunSchedule(TaskEntityContext context, string executionToken)
    {
        ScheduleConfiguration scheduleConfig =
            this.State.ScheduleConfiguration ??
            throw new InvalidOperationException("Schedule configuration is missing.");
        TimeSpan interval = scheduleConfig.Interval;

        if (executionToken != this.State.ExecutionToken)
        {
            this.logger.ScheduleRunCancelled(scheduleConfig.ScheduleId, executionToken);
            return;
        }

        if (this.State.Status != ScheduleStatus.Active)
        {
            string errorMessage = "Schedule must be in Active status to run.";
            Exception exception = new InvalidOperationException(errorMessage);
            this.logger.ScheduleOperationError(scheduleConfig.ScheduleId, nameof(this.RunSchedule), errorMessage);
            throw exception;
        }

        // if endat is set and time now is past endat, do not run
        if (scheduleConfig.EndAt.HasValue && DateTimeOffset.UtcNow > scheduleConfig.EndAt.Value)
        {
            this.logger.ScheduleRunCancelled(scheduleConfig.ScheduleId, executionToken);
            this.State.NextRunAt = null;
            return;
        }

        this.DetermineNextRunTime(scheduleConfig);

        DateTimeOffset currentTime = DateTimeOffset.UtcNow;

        if (this.State.NextRunAt!.Value <= currentTime)
        {
            this.State.NextRunAt = currentTime;
            this.StartOrchestrationIfNotRunning(context);
            this.State.LastRunAt = this.State.NextRunAt;
            this.State.NextRunAt = this.State.LastRunAt.Value + interval;
        }

        context.SignalEntity(
            new EntityInstanceId(
                nameof(Schedule),
                this.State.ScheduleConfiguration.ScheduleId),
            nameof(this.RunSchedule),
            this.State.ExecutionToken,
            new SignalEntityOptions { SignalTime = this.State.NextRunAt.Value });
    }

    static DateTimeOffset? ComputeInitialRunTime(ScheduleConfiguration scheduleConfig)
    {
        if (scheduleConfig.StartImmediatelyIfLate &&
            scheduleConfig.StartAt.HasValue &&
            DateTimeOffset.UtcNow > scheduleConfig.StartAt.Value)
        {
            return DateTimeOffset.UtcNow;
        }

        return scheduleConfig.StartAt ?? DateTimeOffset.UtcNow; // Default to now if StartAt not defined
    }

    static DateTimeOffset ComputeNextRunTime(ScheduleConfiguration scheduleConfig, DateTimeOffset lastRunAt)
    {
        // Calculate number of intervals between last run and now
        TimeSpan timeSinceLastRun = DateTimeOffset.UtcNow - lastRunAt;
        int intervalsElapsed = (int)(timeSinceLastRun.Ticks / scheduleConfig.Interval.Ticks);

        // Compute and return the next run time
        return lastRunAt + TimeSpan.FromTicks(scheduleConfig.Interval.Ticks * (intervalsElapsed + 1));
    }

    void StartOrchestrationIfNotRunning(TaskEntityContext context)
    {
        try
        {
            StartOrchestrationOptions? startOrchestrationOptions = string.IsNullOrEmpty(this.State.ScheduleConfiguration?.OrchestrationInstanceId)
                ? null
                : new StartOrchestrationOptions(this.State.ScheduleConfiguration.OrchestrationInstanceId);
            context.ScheduleNewOrchestration(
                new TaskName(this.State.ScheduleConfiguration!.OrchestrationName),
                this.State.ScheduleConfiguration.OrchestrationInput,
                startOrchestrationOptions);
        }
        catch (Exception ex)
        {
            this.logger.ScheduleOperationError(
                this.State.ScheduleConfiguration!.ScheduleId,
                nameof(this.StartOrchestrationIfNotRunning),
                "Failed to start orchestration",
                ex);
        }
    }

    bool CanTransitionTo(string operationName, ScheduleStatus targetStatus)
    {
        HashSet<ScheduleStatus> validTargetStates;
        ScheduleStatus currentStatus = this.State.Status;

        return ScheduleTransitions.TryGetValidTransitions(operationName, currentStatus, out validTargetStates) &&
               validTargetStates.Contains(targetStatus);
    }

    void TryStatusTransition(string operationName, ScheduleStatus to)
    {
        if (!this.CanTransitionTo(operationName, to))
        {
            this.logger.ScheduleOperationError(this.State.ScheduleConfiguration!.ScheduleId, nameof(this.TryStatusTransition), $"Invalid state transition from {this.State.Status} to {to}");
            throw new ScheduleInvalidTransitionException(this.State.ScheduleConfiguration!.ScheduleId, this.State.Status, to, operationName);
        }

        this.State.Status = to;
    }

    void DetermineNextRunTime(ScheduleConfiguration scheduleConfig)
    {
        if (this.State.NextRunAt.HasValue)
        {
            return; // NextRunAt already set, no need to compute
        }

        // If LastRunAt is not set, determine if we should start immediately or at StartAt
        if (!this.State.LastRunAt.HasValue)
        {
            this.State.NextRunAt = ComputeInitialRunTime(scheduleConfig);
        }
        else
        {
            this.State.NextRunAt = ComputeNextRunTime(scheduleConfig, this.State.LastRunAt.Value);
        }
    }
}
