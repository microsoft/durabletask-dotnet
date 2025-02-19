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
    public IScheduleHandle GetScheduleHandle(string scheduleId)
    {
        return new ScheduleHandle(this.durableTaskClient, scheduleId);
    }

    /// <inheritdoc/>
    public async Task<IScheduleHandle> CreateScheduleAsync(ScheduleCreationOptions scheduleConfigCreateOptions)
    {
        Check.NotNull(scheduleConfigCreateOptions, nameof(scheduleConfigCreateOptions));

        var entityId = new EntityInstanceId(nameof(Schedule), scheduleConfigCreateOptions.ScheduleId);
        await this.durableTaskClient.Entities.SignalEntityAsync(entityId, nameof(Schedule.CreateSchedule), scheduleConfigCreateOptions);

        return new ScheduleHandle(this.durableTaskClient, scheduleConfigCreateOptions.ScheduleId);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<ScheduleDescription>> ListSchedulesAsync()
    {
        var query = new EntityQuery
        {
            InstanceIdStartsWith = $"@{nameof(Schedule)}@",
            IncludeState = false,
        };

        var schedules = new List<ScheduleDescription>();
        await foreach (var metadata in this.durableTaskClient.Entities.GetAllEntitiesAsync<ScheduleState>(query))
        {
            schedules.Add(new ScheduleDescription(metadata.Id.Key));
        }

        return schedules;
    }
}
