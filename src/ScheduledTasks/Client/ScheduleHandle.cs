// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask.ScheduledTasks;

// TODO: Validaiton
// TODO: GET if config is null what to return

/// <summary>
/// Represents a handle to a scheduled task, providing operations for managing the schedule.
/// </summary>
public class ScheduleHandle : IScheduleHandle
{
    readonly DurableTaskClient durableTaskClient;

    /// <summary>
    /// Gets the ID of the schedule.
    /// </summary>
    public string ScheduleId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleHandle"/> class.
    /// </summary>
    /// <param name="client">The durable task client.</param>
    /// <param name="scheduleId">The ID of the schedule.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="client"/> or <paramref name="scheduleId"/> is null.</exception>
    public ScheduleHandle(DurableTaskClient client, string scheduleId)
    {
        client = client ?? throw new ArgumentNullException(nameof(client));
        this.ScheduleId = scheduleId ?? throw new ArgumentNullException(nameof(scheduleId));
    }

    /// <inheritdoc/>
    public async Task<ScheduleDescription> GetAsync()
    {
        Check.NotNullOrEmpty(this.ScheduleId, nameof(this.ScheduleId));

        EntityInstanceId entityId = new EntityInstanceId(nameof(Schedule), this.ScheduleId);
        EntityMetadata<ScheduleState>? metadata = await this.durableTaskClient.Entities.GetEntityAsync<ScheduleState>(entityId);
        if (metadata == null)
        {
            throw new InvalidOperationException($"Schedule with ID {this.ScheduleId} does not exist.");
        }

        ScheduleState state = metadata.State;
        ScheduleConfiguration? config = state.ScheduleConfiguration;
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
        Check.NotNullOrEmpty(this.ScheduleId, nameof(this.ScheduleId));

        var entityId = new EntityInstanceId(nameof(Schedule), this.ScheduleId);
        var metadata = await this.durableTaskClient.Entities.GetEntityAsync<ScheduleState>(entityId);
        if (metadata == null)
        {
            throw new InvalidOperationException($"Schedule with ID {this.ScheduleId} does not exist.");
        }

        await this.durableTaskClient.Entities.SignalEntityAsync(entityId, nameof(Schedule.PauseSchedule));
    }

    /// <inheritdoc/>
    public async Task ResumeAsync()
    {
        Check.NotNullOrEmpty(this.ScheduleId, nameof(this.ScheduleId));

        var entityId = new EntityInstanceId(nameof(Schedule), this.ScheduleId);
        var metadata = await this.durableTaskClient.Entities.GetEntityAsync<ScheduleState>(entityId);
        if (metadata == null)
        {
            throw new InvalidOperationException($"Schedule with ID {this.ScheduleId} does not exist.");
        }

        await this.durableTaskClient.Entities.SignalEntityAsync(entityId, nameof(Schedule.ResumeSchedule));
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(ScheduleUpdateOptions updateOptions)
    {
        Check.NotNull(updateOptions, nameof(updateOptions));

        var entityId = new EntityInstanceId(nameof(Schedule), this.ScheduleId);
        var metadata = await this.durableTaskClient.Entities.GetEntityAsync<ScheduleState>(entityId);
        if (metadata == null)
        {
            throw new InvalidOperationException($"Schedule with ID {this.ScheduleId} does not exist.");
        }

        await this.durableTaskClient.Entities.SignalEntityAsync(entityId, nameof(Schedule.UpdateSchedule), updateOptions);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync()
    {
        Check.NotNullOrEmpty(this.ScheduleId, nameof(this.ScheduleId));

        var entityId = new EntityInstanceId(nameof(Schedule), this.ScheduleId);
        var metadata = await this.durableTaskClient.Entities.GetEntityAsync<ScheduleState>(entityId);
        if (metadata == null)
        {
            throw new InvalidOperationException($"Schedule with ID {this.ScheduleId} does not exist.");
        }

        await this.durableTaskClient.Entities.SignalEntityAsync(entityId, "delete");
    }
}
