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
public class ScheduledTaskClient(DurableTaskClient durableTaskClient, ILogger logger) : IScheduledTaskClient
{
    readonly DurableTaskClient durableTaskClient = Check.NotNull(durableTaskClient, nameof(durableTaskClient));
    readonly ILogger logger = Check.NotNull(logger, nameof(logger));

    /// <inheritdoc/>
    public async Task<IScheduleHandle> CreateScheduleAsync(ScheduleCreationOptions creationOptions, CancellationToken cancellation = default)
    {
        Check.NotNull(creationOptions, nameof(creationOptions));
        this.logger.ClientCreatingSchedule(creationOptions);

        try
        {
            string scheduleId = creationOptions.ScheduleId;
            EntityInstanceId entityId = new EntityInstanceId(nameof(Schedule), scheduleId);

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
    public AsyncPageable<ScheduleDescription> ListSchedulesAsync(ScheduleQuery? filter = null)
    {
        // Create an async pageable using the Pageable.Create helper
        return Pageable.Create(async (continuationToken, pageSize, cancellation) =>
        {
            try
            {
                // TODO: map to entity query last modified from/to filters
                EntityQuery query = new EntityQuery
                {
                    InstanceIdStartsWith = filter?.ScheduleIdPrefix ?? nameof(Schedule),
                    IncludeState = true,
                    PageSize = filter?.PageSize ?? ScheduleQuery.DefaultPageSize,
                    ContinuationToken = continuationToken,
                };

                // Get one page of entities
                IAsyncEnumerable<Page<EntityMetadata<ScheduleState>>> entityPages =
                    this.durableTaskClient.Entities.GetAllEntitiesAsync<ScheduleState>(query).AsPages();

                await foreach (Page<EntityMetadata<ScheduleState>> entityPage in entityPages)
                {
                    List<ScheduleDescription> schedules = entityPage.Values
                        .Where(metadata =>
                            (!filter?.Status.HasValue ?? true || metadata.State.Status == filter.Status.Value) &&
                            (filter?.CreatedFrom.HasValue != true || metadata.State.ScheduleCreatedAt > filter.CreatedFrom) &&
                            (filter?.CreatedTo.HasValue != true || metadata.State.ScheduleCreatedAt < filter.CreatedTo))
                        .Select(metadata =>
                        {
                            ScheduleState state = metadata.State;
                            ScheduleConfiguration config = state.ScheduleConfiguration!;
                            return new ScheduleDescription
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
                            };
                        })
                        .ToList();

                    return new Page<ScheduleDescription>(schedules, entityPage.ContinuationToken);
                }

                // Return empty page if no results
                return new Page<ScheduleDescription>(new List<ScheduleDescription>(), null);
            }
            catch (OperationCanceledException e)
            {
                throw new OperationCanceledException(
                    $"The {nameof(this.ListSchedulesAsync)} operation was canceled.", e, e.CancellationToken);
            }
        });
    }
}
