// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Client for managing scheduled tasks in a Durable Task application.
/// </summary>
public class ScheduledTaskClient : IScheduledTaskClient
{
    readonly DurableTaskClient durableTaskClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduledTaskClient"/> class.
    /// </summary>
    /// <param name="durableTaskClient">The Durable Task client to use for orchestration operations.</param>
    public ScheduledTaskClient(DurableTaskClient durableTaskClient)
    {
        this.durableTaskClient = Check.NotNull(durableTaskClient, nameof(durableTaskClient));
    }

    /// <inheritdoc/>
    public async Task<string> CreateScheduleAsync(ScheduleCreationOptions scheduleConfigCreateOptions)
    {
        Check.NotNull(scheduleConfigCreateOptions, nameof(scheduleConfigCreateOptions));

        var entityId = new EntityInstanceId(nameof(Schedule), scheduleConfigCreateOptions.ScheduleId);
        await this.durableTaskClient.Entities.SignalEntityAsync(entityId, nameof(Schedule.CreateSchedule), scheduleConfigCreateOptions);

        return scheduleConfigCreateOptions.ScheduleId;
    }

    /// <inheritdoc/>
    public async Task DeleteScheduleAsync(string scheduleId)
    {
        Check.NotNullOrEmpty(scheduleId, nameof(scheduleId));

        var entityId = new EntityInstanceId(nameof(Schedule), scheduleId);
        var metadata = await this.durableTaskClient.Entities.GetEntityAsync<ScheduleState>(entityId);
        if (metadata == null)
        {
            throw new InvalidOperationException($"Schedule with ID {scheduleId} does not exist.");
        }

        await this.durableTaskClient.Entities.SignalEntityAsync(entityId, "delete");
    }

    /// <inheritdoc/>
    public async Task<ScheduleDescription> GetScheduleAsync(string scheduleId)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<string>> ListSchedulesAsync()
    {
        var query = new EntityQuery
        {
            InstanceIdStartsWith = $"@{nameof(Schedule)}@",
            IncludeState = false,
        };

        var scheduleIds = new List<string>();
        await foreach (var metadata in this.durableTaskClient.Entities.GetAllEntitiesAsync<ScheduleState>(query))
        {
            scheduleIds.Add(metadata.Id.Key);
        }

        return scheduleIds;
    }

    /// <inheritdoc/>
    public async Task PauseScheduleAsync(string scheduleId)
    {
        Check.NotNullOrEmpty(scheduleId, nameof(scheduleId));

        var entityId = new EntityInstanceId(nameof(Schedule), scheduleId);
        var metadata = await this.durableTaskClient.Entities.GetEntityAsync<ScheduleState>(entityId);
        if (metadata == null)
        {
            throw new InvalidOperationException($"Schedule with ID {scheduleId} does not exist.");
        }

        await this.durableTaskClient.Entities.SignalEntityAsync(entityId, nameof(Schedule.PauseSchedule));
    }

    /// <inheritdoc/>
    public async Task ResumeScheduleAsync(string scheduleId)
    {
        Check.NotNullOrEmpty(scheduleId, nameof(scheduleId));

        var entityId = new EntityInstanceId(nameof(Schedule), scheduleId);
        var metadata = await this.durableTaskClient.Entities.GetEntityAsync<ScheduleState>(entityId);
        if (metadata == null)
        {
            throw new InvalidOperationException($"Schedule with ID {scheduleId} does not exist.");
        }

        await this.durableTaskClient.Entities.SignalEntityAsync(entityId, nameof(Schedule.ResumeSchedule));
    }

    /// <inheritdoc/>
    public async Task UpdateScheduleAsync(string scheduleId, ScheduleUpdateOptions scheduleConfigurationUpdateOptions)
    {
        Check.NotNullOrEmpty(scheduleId, nameof(scheduleId));
        Check.NotNull(scheduleConfigurationUpdateOptions, nameof(scheduleConfigurationUpdateOptions));

        var entityId = new EntityInstanceId(nameof(Schedule), scheduleId);
        var metadata = await this.durableTaskClient.Entities.GetEntityAsync<ScheduleState>(entityId);
        if (metadata == null)
        {
            throw new InvalidOperationException($"Schedule with ID {scheduleId} does not exist.");
        }

        // Convert ScheduleConfiguration to ScheduleConfigurationUpdateOptions
        var updateOptions = new ScheduleUpdateOptions
        {
            OrchestrationName = scheduleConfigurationUpdateOptions.OrchestrationName,
            OrchestrationInput = scheduleConfigurationUpdateOptions.OrchestrationInput,
            OrchestrationInstanceId = scheduleConfigurationUpdateOptions.OrchestrationInstanceId,
            StartAt = scheduleConfigurationUpdateOptions.StartAt,
            EndAt = scheduleConfigurationUpdateOptions.EndAt,
            Interval = scheduleConfigurationUpdateOptions.Interval,
            CronExpression = scheduleConfigurationUpdateOptions.CronExpression,
            MaxOccurrence = scheduleConfigurationUpdateOptions.MaxOccurrence,
            StartImmediatelyIfLate = scheduleConfigurationUpdateOptions.StartImmediatelyIfLate,
        };

        await this.durableTaskClient.Entities.SignalEntityAsync(entityId, nameof(Schedule.UpdateSchedule), updateOptions);
    }
}
