// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Client for managing scheduled tasks in a Durable Task application.
/// </summary>
public class ScheduledTaskClient : IScheduledTaskClient
{
    private readonly DurableTaskClient durableTaskClient;
    private readonly ILogger<ScheduledTaskClient> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduledTaskClient"/> class.
    /// </summary>
    /// <param name="durableTaskClient">The Durable Task client to use for orchestration operations.</param>
    /// <param name="logger">The logger to use for logging operations.</param>
    public ScheduledTaskClient(DurableTaskClient durableTaskClient, ILogger<ScheduledTaskClient> logger)
    {
        this.durableTaskClient = Check.NotNull(durableTaskClient, nameof(durableTaskClient));
        this.logger = Check.NotNull(logger, nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<string> CreateScheduleAsync(ScheduleConfiguration scheduleConfig)
    {
        Check.NotNull(scheduleConfig, nameof(scheduleConfig));
        this.logger.LogInformation("Creating new schedule with ID {ScheduleId}", scheduleConfig.ScheduleId);

        var entityId = new EntityInstanceId(nameof(Schedule), scheduleConfig.ScheduleId);
        await this.durableTaskClient.Entities.SignalEntityAsync(entityId, nameof(Schedule.CreateSchedule), scheduleConfig);
        
        return scheduleConfig.ScheduleId;
    }

    /// <inheritdoc/>
    public async Task DeleteScheduleAsync(string scheduleId)
    {
        Check.NotNullOrEmpty(scheduleId, nameof(scheduleId));
        this.logger.LogInformation("Deleting schedule with ID {ScheduleId}", scheduleId);

        var entityId = new EntityInstanceId(nameof(Schedule), scheduleId);
        var metadata = await this.durableTaskClient.Entities.GetEntityAsync<ScheduleState>(entityId);
        if (metadata == null)
        {
            throw new InvalidOperationException($"Schedule with ID {scheduleId} does not exist.");
        }

        await this.durableTaskClient.Entities.SignalEntityAsync(entityId, "delete");
    }

    /// <inheritdoc/>
    public async Task<ScheduleState> GetScheduleAsync(string scheduleId)
    {
        Check.NotNullOrEmpty(scheduleId, nameof(scheduleId));
        this.logger.LogInformation("Getting schedule with ID {ScheduleId}", scheduleId);

        var entityId = new EntityInstanceId(nameof(Schedule), scheduleId);
        var metadata = await this.durableTaskClient.Entities.GetEntityAsync<ScheduleState>(entityId);
        
        if (metadata == null || !metadata.IncludesState)
        {
            throw new InvalidOperationException($"Schedule with ID {scheduleId} does not exist.");
        }

        return metadata.State;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<string>> ListSchedulesAsync()
    {
        this.logger.LogInformation("Listing all schedules");

        var query = new EntityQuery
        {
            EntityName = nameof(Schedule),
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
        this.logger.LogInformation("Pausing schedule with ID {ScheduleId}", scheduleId);

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
        this.logger.LogInformation("Resuming schedule with ID {ScheduleId}", scheduleId);

        var entityId = new EntityInstanceId(nameof(Schedule), scheduleId);
        var metadata = await this.durableTaskClient.Entities.GetEntityAsync<ScheduleState>(entityId);
        if (metadata == null)
        {
            throw new InvalidOperationException($"Schedule with ID {scheduleId} does not exist.");
        }

        await this.durableTaskClient.Entities.SignalEntityAsync(entityId, nameof(Schedule.ResumeSchedule));
    }

    /// <inheritdoc/>
    public async Task UpdateScheduleAsync(string scheduleId, ScheduleConfiguration scheduleConfig)
    {
        Check.NotNullOrEmpty(scheduleId, nameof(scheduleId));
        Check.NotNull(scheduleConfig, nameof(scheduleConfig));
        this.logger.LogInformation("Updating schedule with ID {ScheduleId}", scheduleId);

        var entityId = new EntityInstanceId(nameof(Schedule), scheduleId);
        var metadata = await this.durableTaskClient.Entities.GetEntityAsync<ScheduleState>(entityId);
        if (metadata == null)
        {
            throw new InvalidOperationException($"Schedule with ID {scheduleId} does not exist.");
        }

        // Convert ScheduleConfiguration to ScheduleConfigurationUpdateOptions
        var updateOptions = new ScheduleConfigurationUpdateOptions
        {
            OrchestrationName = scheduleConfig.OrchestrationName,
            OrchestrationInput = scheduleConfig.OrchestrationInput,
            OrchestrationInstanceId = scheduleConfig.OrchestrationInstanceId,
            StartAt = scheduleConfig.StartAt,
            EndAt = scheduleConfig.EndAt,
            Interval = scheduleConfig.Interval,
            CronExpression = scheduleConfig.CronExpression,
            MaxOccurrence = scheduleConfig.MaxOccurrence,
            StartImmediatelyIfLate = scheduleConfig.StartImmediatelyIfLate
        };

        await this.durableTaskClient.Entities.SignalEntityAsync(entityId, nameof(Schedule.UpdateSchedule), updateOptions);
    }
}
