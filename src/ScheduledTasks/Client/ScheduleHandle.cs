// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.ScheduledTasks;

// TODO: Validaiton

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
        this.EntityId = new EntityInstanceId(nameof(Schedule), this.ScheduleId);
    }

    /// <summary>
    /// Gets the ID of the schedule.
    /// </summary>
    public string ScheduleId { get; }

    /// <summary>
    /// Gets the entity ID of the schedule.
    /// </summary>
    public EntityInstanceId EntityId { get; }

    /// <inheritdoc/>
    public async Task<ScheduleDescription> DescribeAsync(bool includeFullActivityLogs = false)
    {
        Check.NotNullOrEmpty(this.ScheduleId, nameof(this.ScheduleId));

        EntityInstanceId entityId = new EntityInstanceId(nameof(Schedule), this.ScheduleId);
        EntityMetadata<ScheduleState>? metadata =
            await this.durableTaskClient.Entities.GetEntityAsync<ScheduleState>(entityId);
        if (metadata == null)
        {
            throw new ScheduleNotFoundException(this.ScheduleId);
        }

        ScheduleState state = metadata.State;

        ScheduleConfiguration? config = state.ScheduleConfiguration;

        IReadOnlyCollection<ScheduleActivityLog> activityLogs =
            includeFullActivityLogs ? state.ActivityLogs : state.ActivityLogs.TakeLast(1).ToArray();

        return new ScheduleDescription
        {
            ScheduleId = this.ScheduleId,
            OrchestrationName = config?.OrchestrationName,
            OrchestrationInput = config?.OrchestrationInput,
            OrchestrationInstanceId = config?.OrchestrationInstanceId,
            StartAt = config?.StartAt,
            EndAt = config?.EndAt,
            Interval = config?.Interval,
            StartImmediatelyIfLate = config?.StartImmediatelyIfLate,
            Status = state.Status,
            ExecutionToken = state.ExecutionToken,
            LastRunAt = state.LastRunAt,
            NextRunAt = state.NextRunAt,
            ActivityLogs = activityLogs,
        };
    }

    /// <inheritdoc/>
    public async Task<IScheduleWaiter> PauseAsync()
    {
        this.logger.ClientPausingSchedule(this.ScheduleId);

        await this.durableTaskClient.Entities.SignalEntityAsync(this.EntityId, nameof(Schedule.PauseSchedule));
        return new ScheduleWaiter(this, nameof(Schedule.PauseSchedule));
    }

    /// <inheritdoc/>
    public async Task<IScheduleWaiter> ResumeAsync()
    {
        this.logger.ClientResumingSchedule(this.ScheduleId);

        await this.durableTaskClient.Entities.SignalEntityAsync(this.EntityId, nameof(Schedule.ResumeSchedule));

        return new ScheduleWaiter(this, nameof(Schedule.ResumeSchedule));
    }

    /// <inheritdoc/>
    public async Task<IScheduleWaiter> UpdateAsync(ScheduleUpdateOptions updateOptions)
    {
        this.logger.ClientUpdatingSchedule(this.ScheduleId);
        Check.NotNull(updateOptions, nameof(updateOptions));
        await this.durableTaskClient.Entities.SignalEntityAsync(this.EntityId, nameof(Schedule.UpdateSchedule), updateOptions);
        return new ScheduleWaiter(this, nameof(Schedule.UpdateSchedule));
    }

    /// <inheritdoc/>
    public async Task<IScheduleWaiter> DeleteAsync()
    {
        this.logger.ClientDeletingSchedule(this.ScheduleId);

        await this.durableTaskClient.Entities.SignalEntityAsync(this.EntityId, "delete");
        return new ScheduleWaiter(this, "delete");
    }
}
