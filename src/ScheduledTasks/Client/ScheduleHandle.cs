// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.ScheduledTasks;

// TODO: Validaiton
// TODO: GET if config is null what to return

/// <summary>
/// Represents a handle to a scheduled task, providing operations for managing the schedule.
/// </summary>
public class ScheduleHandle : IScheduleHandle
{
    readonly DurableTaskClient durableTaskClient;
    readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleHandle"/> class.
    /// </summary>
    /// <param name="client">The durable task client.</param>
    /// <param name="scheduleId">The ID of the schedule.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="client"/> or <paramref name="scheduleId"/> is null.</exception>
    public ScheduleHandle(DurableTaskClient client, string scheduleId, ILogger logger)
    {
        this.durableTaskClient = client ?? throw new ArgumentNullException(nameof(client));
        this.ScheduleId = scheduleId ?? throw new ArgumentNullException(nameof(scheduleId));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the ID of the schedule.
    /// </summary>
    public string ScheduleId { get; }

    /// <inheritdoc/>
    public async Task<ScheduleDescription> DescribeAsync()
    {
        Check.NotNullOrEmpty(this.ScheduleId, nameof(this.ScheduleId));
        this.logger.ClientDescribingSchedule(this.ScheduleId);

        EntityInstanceId entityId = new EntityInstanceId(nameof(Schedule), this.ScheduleId);
        EntityMetadata<ScheduleState>? metadata =
            await this.durableTaskClient.Entities.GetEntityAsync<ScheduleState>(entityId);
        if (metadata == null)
        {
            throw new ScheduleNotFoundException(this.ScheduleId);
        }

        ScheduleState state = metadata.State;
        if (state.Status == ScheduleStatus.Uninitialized)
        {
            throw new ScheduleStillBeingProvisionedException(this.ScheduleId);
        }

        // this should never happen
        ScheduleConfiguration? config = state.ScheduleConfiguration;
        if (config == null)
        {
            throw new ScheduleInternalException(
                this.ScheduleId,
                $"Schedule configuration is not available even though the schedule status is {state.Status}.");
        }

        return new ScheduleDescription(
            this.ScheduleId,
            config.OrchestrationName,
            config.OrchestrationInput,
            config.OrchestrationInstanceId,
            config.StartAt,
            config.EndAt,
            config.Interval,
            config.CronExpression,
            config.MaxOccurrence,
            config.StartImmediatelyIfLate,
            state.Status,
            state.ExecutionToken,
            state.LastRunAt,
            state.NextRunAt);
    }

    /// <inheritdoc/>
    public async Task PauseAsync()
    {
        this.logger.ClientPausingSchedule(this.ScheduleId);
        Check.NotNullOrEmpty(this.ScheduleId, nameof(this.ScheduleId));

        EntityInstanceId entityId = new EntityInstanceId(nameof(Schedule), this.ScheduleId);
        EntityMetadata<ScheduleState>? metadata = await this.durableTaskClient.Entities.GetEntityAsync<ScheduleState>(entityId);
        if (metadata == null)
        {
            throw new ScheduleNotFoundException(this.ScheduleId);
        }

        await this.durableTaskClient.Entities.SignalEntityAsync(entityId, nameof(Schedule.PauseSchedule));
    }

    /// <inheritdoc/>
    public async Task ResumeAsync()
    {
        this.logger.ClientResumingSchedule(this.ScheduleId);
        Check.NotNullOrEmpty(this.ScheduleId, nameof(this.ScheduleId));

        EntityInstanceId entityId = new EntityInstanceId(nameof(Schedule), this.ScheduleId);
        EntityMetadata<ScheduleState>? metadata = await this.durableTaskClient.Entities.GetEntityAsync<ScheduleState>(entityId);
        if (metadata == null)
        {
            throw new ScheduleNotFoundException(this.ScheduleId);
        }

        await this.durableTaskClient.Entities.SignalEntityAsync(entityId, nameof(Schedule.ResumeSchedule));
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(ScheduleUpdateOptions updateOptions)
    {
        this.logger.ClientUpdatingSchedule(this.ScheduleId);
        Check.NotNull(updateOptions, nameof(updateOptions));

        EntityInstanceId entityId = new EntityInstanceId(nameof(Schedule), this.ScheduleId);
        EntityMetadata<ScheduleState>? metadata = await this.durableTaskClient.Entities.GetEntityAsync<ScheduleState>(entityId);
        if (metadata == null)
        {
            throw new ScheduleNotFoundException(this.ScheduleId);
        }

        await this.durableTaskClient.Entities.SignalEntityAsync(entityId, nameof(Schedule.UpdateSchedule), updateOptions);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync()
    {
        this.logger.ClientDeletingSchedule(this.ScheduleId);
        Check.NotNullOrEmpty(this.ScheduleId, nameof(this.ScheduleId));

        EntityInstanceId entityId = new EntityInstanceId(nameof(Schedule), this.ScheduleId);
        EntityMetadata<ScheduleState>? metadata = await this.durableTaskClient.Entities.GetEntityAsync<ScheduleState>(entityId);
        if (metadata == null)
        {
            throw new ScheduleNotFoundException(this.ScheduleId);
        }

        await this.durableTaskClient.Entities.SignalEntityAsync(entityId, "delete");
    }
}
