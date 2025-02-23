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
        this.logger.ClientCreatingSchedule(creationOptions);

        try
        {
            string scheduleId = creationOptions.ScheduleId;
            EntityInstanceId entityId = new EntityInstanceId(nameof(Schedule), scheduleId);

            // Check if schedule already exists
            bool scheduleExists = await this.CheckScheduleExists(scheduleId, cancellation);
            if (scheduleExists)
            {
                throw new ScheduleAlreadyExistException(scheduleId);
            }

            // Call the orchestrator to create the schedule
            ScheduleOperationRequest request = new ScheduleOperationRequest(entityId, nameof(Schedule.CreateSchedule), creationOptions);
            string instanceId = await this.durableTaskClient.ScheduleNewOrchestrationInstanceAsync(
                new TaskName(nameof(ExecuteScheduleOperationOrchestrator)),
                request,
                cancellation);

            // Wait for the orchestration to complete
            OrchestrationMetadata state = await this.durableTaskClient.WaitForInstanceCompletionAsync(instanceId, true, cancellation);

            if (state.RuntimeStatus != OrchestrationRuntimeStatus.Completed)
            {
                throw new InvalidOperationException($"Failed to create schedule '{scheduleId}': {state.FailureDetails?.ErrorMessage ?? string.Empty}");
            }

            // Return a handle to the schedule
            return new ScheduleHandle(this.durableTaskClient, scheduleId, this.logger);
        }
        catch (OperationCanceledException ex)
        {
            this.logger.ClientError(
                nameof(this.CreateScheduleAsync),
                creationOptions.ScheduleId,
                ex);

            throw new OperationCanceledException(
                $"The {nameof(this.CreateScheduleAsync)} operation was canceled.",
                null,
                cancellation);
        }
    }

    /// <inheritdoc/>
    public async Task<ScheduleDescription?> GetScheduleAsync(string scheduleId, CancellationToken cancellation = default)
    {
        Check.NotNullOrEmpty(scheduleId, nameof(scheduleId));

        try
        {
            EntityInstanceId entityId = new EntityInstanceId(nameof(Schedule), scheduleId);
            EntityMetadata<ScheduleState>? metadata = await this.durableTaskClient.Entities.GetEntityAsync<ScheduleState>(entityId, cancellation);

            if (metadata == null || metadata.State.Status == ScheduleStatus.Uninitialized)
            {
                return null;
            }

            ScheduleState state = metadata.State;
            ScheduleConfiguration? config = state.ScheduleConfiguration;

            return new ScheduleDescription
            {
                ScheduleId = scheduleId,
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
            };
        }
        catch (OperationCanceledException ex)
        {
            this.logger.ClientError(
                nameof(this.GetScheduleAsync),
                scheduleId,
                ex);

            throw new OperationCanceledException(
                $"The {nameof(this.GetScheduleAsync)} operation was canceled.",
                null,
                cancellation);
        }
    }

    /// <inheritdoc/>
    public IScheduleHandle GetScheduleHandle(string scheduleId)
    {
        Check.NotNullOrEmpty(scheduleId, nameof(scheduleId));
        return new ScheduleHandle(this.durableTaskClient, scheduleId, this.logger);
    }

    /// <inheritdoc/>
    public Task<AsyncPageable<ScheduleDescription>> ListSchedulesAsync(ScheduleQuery? filter = null)
    {
        // TODO: map to entity query last modified from/to filters
        EntityQuery query = new EntityQuery
        {
            InstanceIdStartsWith = filter?.ScheduleIdPrefix ?? nameof(Schedule),
            IncludeState = true,
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

    /// <inheritdoc/>
    public Task<AsyncPageable<string>> ListScheduleIdsAsync(ScheduleQuery? filter = null)
    {
        EntityQuery query = new EntityQuery
        {
            InstanceIdStartsWith = filter?.ScheduleIdPrefix ?? nameof(Schedule),
            IncludeState = false, // We don't need the state since we only want IDs
            PageSize = filter?.PageSize ?? ScheduleQuery.DefaultPageSize,
            ContinuationToken = filter?.ContinuationToken,
        };

        // Create an async pageable using the Pageable.Create helper
        return Task.FromResult(Pageable.Create(async (continuationToken, pageSize, cancellation) =>
        {
            try
            {
                List<string> scheduleIds = new List<string>();

                await foreach (EntityMetadata metadata in this.durableTaskClient.Entities.GetAllEntitiesAsync(query))
                {
                    // Extract just the schedule ID from the entity ID
                    scheduleIds.Add(metadata.Id.Key);
                }

                return new Page<string>(scheduleIds, continuationToken);
            }
            catch (OperationCanceledException e)
            {
                throw new OperationCanceledException(
                    $"The {nameof(this.ListScheduleIdsAsync)} operation was canceled.", e, e.CancellationToken);
            }
        }));
    }

    /// <summary>
    /// Checks if a schedule with the specified ID exists.
    /// </summary>
    /// <param name="scheduleId">The ID of the schedule to check.</param>
    /// <param name="cancellation">Optional cancellation token.</param>
    /// <returns>True if the schedule exists, false otherwise.</returns>
    async Task<bool> CheckScheduleExists(string scheduleId, CancellationToken cancellation = default)
    {
        Check.NotNullOrEmpty(scheduleId, nameof(scheduleId));

        try
        {
            EntityInstanceId entityId = new EntityInstanceId(nameof(Schedule), scheduleId);
            EntityMetadata? metadata = await this.durableTaskClient.Entities.GetEntityAsync(entityId, false, cancellation);

            return metadata != null;
        }
        catch (OperationCanceledException e)
        {
            this.logger.ClientError(
                nameof(this.CheckScheduleExists),
                scheduleId,
                e);

            throw new OperationCanceledException(
                $"The {nameof(this.CheckScheduleExists)} operation was canceled.", e, e.CancellationToken);
        }
    }
}
