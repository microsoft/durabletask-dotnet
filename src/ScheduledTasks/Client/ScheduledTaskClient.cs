// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.ScheduledTasks;

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
    public async Task<IEnumerable<ScheduleDescription>> ListSchedulesAsync(bool includeFullActivityLogs = false)
    {
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

                IReadOnlyCollection<ScheduleActivityLog> activityLogs =
                    includeFullActivityLogs ? metadata.State.ActivityLogs : metadata.State.ActivityLogs.TakeLast(1).ToArray();

                schedules.Add(new ScheduleDescription
                {
                    ScheduleId = metadata.Id.Key,
                    OrchestrationName = config.OrchestrationName,
                    OrchestrationInput = config.OrchestrationInput,
                    OrchestrationInstanceId = config.OrchestrationInstanceId,
                    StartAt = config.StartAt,
                    EndAt = config.EndAt,
                    Interval = config.Interval,
                    StartImmediatelyIfLate = config.StartImmediatelyIfLate,
                    Status = metadata.State.Status,
                    ExecutionToken = metadata.State.ExecutionToken,
                    LastRunAt = metadata.State.LastRunAt,
                    NextRunAt = metadata.State.NextRunAt,
                    ActivityLogs = activityLogs,
                });
            }
        }

        return schedules;
    }
}
