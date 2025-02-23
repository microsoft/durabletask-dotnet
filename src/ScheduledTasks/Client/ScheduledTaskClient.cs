// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
    public async Task<ScheduleHandle> CreateScheduleAsync(ScheduleCreationOptions creationOptions, CancellationToken cancellation = default)
    {
        Check.NotNull(creationOptions, nameof(creationOptions));

        string scheduleId = creationOptions.ScheduleId;
        EntityInstanceId entityId = new EntityInstanceId(nameof(Schedule), scheduleId);

        try
        {
            // Call the orchestrator to create the schedule
            ScheduleOperationRequest request = new ScheduleOperationRequest(entityId, nameof(Schedule.CreateSchedule), creationOptions);
            await this.durableTaskClient.ScheduleNewOrchestrationInstanceAsync(
                new TaskName(nameof(ExecuteScheduleOperationOrchestrator)),
                request,
                cancellation);

            // Return a handle to the schedule
            return new ScheduleHandle(this.durableTaskClient, scheduleId, this.logger);
        }
        catch (Exception ex)
        {
            this.logger.ClientError($"Failed to create schedule: {ex.Message}", scheduleId);
            throw;
        }
    }

    /// <inheritdoc/>
    public IScheduleHandle GetScheduleHandle(string scheduleId) => new ScheduleHandle(this.durableTaskClient, scheduleId, this.logger);

    /// <inheritdoc/>
    public Task<AsyncPageable<ScheduleDescription>> ListSchedulesAsync(ScheduleQuery? filter = null)
    {
        // TODO: map to entity query last modified from/to filters
        EntityQuery query = new EntityQuery
        {
            InstanceIdStartsWith = filter?.ScheduleIdPrefix ?? nameof(Schedule),
            IncludeState = filter?.ReturnIdsOnly ?? true,
            PageSize = filter?.PageSize ?? ScheduleQuery.DefaultPageSize,
            ContinuationToken = filter?.ContinuationToken,
        };

        // Create an async pageable using the Pageable.Create helper
        return Task.FromResult(Pageable.Create(async (continuationToken, pageSize, cancellation) =>
        {
            try
            {
                List<ScheduleDescription> schedules = new List<ScheduleDescription>();

                await foreach (EntityMetadata<ScheduleState> metadata in this.durableTaskClient.Entities.GetAllEntitiesAsync<ScheduleState>(query))
                {
                    ScheduleState state = metadata.State;
                    if (state.Status == ScheduleStatus.Uninitialized)
                    {
                        continue;
                    }

                    // Skip if status filter is specified and doesn't match
                    if (filter?.Status.HasValue == true && state.Status != filter.Status.Value)
                    {
                        continue;
                    }

                    // Skip if created time filter is specified and doesn't match
                    if (filter?.CreatedFrom.HasValue == true && state.ScheduleCreatedAt <= filter.CreatedFrom)
                    {
                        continue;
                    }

                    if (filter?.CreatedTo.HasValue == true && state.ScheduleCreatedAt >= filter.CreatedTo)
                    {
                        continue;
                    }

                    ScheduleConfiguration config = state.ScheduleConfiguration!;

                    IReadOnlyCollection<ScheduleActivityLog> activityLogs =
                        filter?.IncludeFullActivityLogs == true ?
                            state.ActivityLogs :
                            state.ActivityLogs.TakeLast(1).ToArray();

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
                        Status = state.Status,
                        ExecutionToken = state.ExecutionToken,
                        LastRunAt = state.LastRunAt,
                        NextRunAt = state.NextRunAt,
                        ActivityLogs = activityLogs,
                    });
                }

                return new Page<ScheduleDescription>(schedules, continuationToken);
            }
            catch (OperationCanceledException e)
            {
                throw new OperationCanceledException(
                    $"The {nameof(this.ListSchedulesAsync)} operation was canceled.", e, e.CancellationToken);
            }
        }));
    }
}
