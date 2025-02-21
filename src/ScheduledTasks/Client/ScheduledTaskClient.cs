// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.ScheduledTasks;

// TODO: validation

/// <summary>
/// Client for managing scheduled tasks in a Durable Task application.
/// </summary>
public class ScheduledTaskClient : IScheduledTaskClient
{
    readonly DurableTaskClient durableTaskClient;
    readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduledTaskClient"/> class.
    /// </summary>
    /// <param name="durableTaskClient">The Durable Task client to use for orchestration operations.</param>
    /// <param name="logger">logger.</param>
    public ScheduledTaskClient(DurableTaskClient durableTaskClient, ILogger logger)
    {
        this.durableTaskClient = Check.NotNull(durableTaskClient, nameof(durableTaskClient));
        this.logger = Check.NotNull(logger, nameof(logger));
    }

    /// <inheritdoc/>
    public IScheduleHandle GetScheduleHandle(string scheduleId)
    {
        this.logger.ClientGettingScheduleHandle(scheduleId);
        return new ScheduleHandle(this.durableTaskClient, scheduleId, this.logger);
    }

    /// <inheritdoc/>
    public async Task<IScheduleHandle> CreateScheduleAsync(ScheduleCreationOptions scheduleConfigCreateOptions)
    {
        this.logger.ClientCreatingSchedule(scheduleConfigCreateOptions);
        Check.NotNull(scheduleConfigCreateOptions, nameof(scheduleConfigCreateOptions));

        EntityInstanceId entityId = new EntityInstanceId(nameof(Schedule), scheduleConfigCreateOptions.ScheduleId);

        await this.durableTaskClient.Entities.SignalEntityAsync(entityId, nameof(Schedule.CreateSchedule), scheduleConfigCreateOptions);

        return new ScheduleHandle(this.durableTaskClient, scheduleConfigCreateOptions.ScheduleId, this.logger);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<ScheduleDescription>> ListSchedulesAsync(bool includeFullActivityLogs = false)
    {
        this.logger.ClientListingSchedules();
        EntityQuery query = new EntityQuery
        {
            InstanceIdStartsWith = nameof(Schedule), // Automatically ensures correct formatting
            IncludeState = true,
        };

        List<ScheduleDescription> schedules = new List<ScheduleDescription>();

        await foreach (EntityMetadata<ScheduleState> metadata in this.durableTaskClient.Entities.GetAllEntitiesAsync<ScheduleState>(query))
        {
            if (metadata.State.Status != ScheduleStatus.Uninitialized)
            {
                ScheduleConfiguration config = metadata.State.ScheduleConfiguration!;
                IReadOnlyCollection<ScheduleActivityLog> activityLogs = metadata.State.ActivityLogs;
                if (!includeFullActivityLogs && activityLogs.Count != 0)
                {
                    activityLogs = new[] { activityLogs.Last() };
                }

                schedules.Add(new ScheduleDescription(
                    metadata.Id.Key,
                    config.OrchestrationName,
                    config.OrchestrationInput,
                    config.OrchestrationInstanceId,
                    config.StartAt,
                    config.EndAt,
                    config.Interval,
                    config.CronExpression,
                    config.MaxOccurrence,
                    config.StartImmediatelyIfLate,
                    metadata.State.Status,
                    metadata.State.ExecutionToken,
                    metadata.State.LastRunAt,
                    metadata.State.NextRunAt,
                    activityLogs));
            }
        }

        return schedules;
    }
}
