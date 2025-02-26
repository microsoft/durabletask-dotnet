// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Client for managing scheduled tasks in a Durable Task application.
/// </summary>
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public class ScheduledTaskClientImpl(DurableTaskClient durableTaskClient, ILogger logger) : ScheduledTaskClient
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
    readonly DurableTaskClient durableTaskClient = Check.NotNull(durableTaskClient, nameof(durableTaskClient));
    readonly ILogger logger = Check.NotNull(logger, nameof(logger));

    /// <inheritdoc/>
    public override async Task<ScheduleClient> CreateScheduleAsync(ScheduleCreationOptions creationOptions, CancellationToken cancellation = default)
    {
        Check.NotNull(creationOptions, nameof(creationOptions));
        this.logger.ClientCreatingSchedule(creationOptions);

        try
        {
            // Create schedule client instance
            ScheduleClient scheduleClient = new ScheduleClientImpl(this.durableTaskClient, creationOptions.ScheduleId, this.logger);

            // Create the schedule using the client
            await scheduleClient.CreateAsync(creationOptions, cancellation);

            return scheduleClient;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // the operation was cancelled as requested. No need to log this.
            throw;
        }
        catch (Exception ex)
        {
            this.logger.ClientError(
                nameof(this.CreateScheduleAsync),
                creationOptions.ScheduleId,
                ex);

            throw;
        }
    }

    /// <inheritdoc/>
    public override async Task<ScheduleDescription?> GetScheduleAsync(string scheduleId, CancellationToken cancellation = default)
    {
        Check.NotNullOrEmpty(scheduleId, nameof(scheduleId));

        try
        {
            // Get schedule client first
            ScheduleClient scheduleClient = this.GetScheduleClient(scheduleId);

            // Call DescribeAsync which handles all the entity state mapping
            return await scheduleClient.DescribeAsync(cancellation);
        }
        catch (ScheduleNotFoundException)
        {
            // Return null if schedule not found
            return null;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // the operation was cancelled as requested. No need to log this.
            throw;
        }
        catch (Exception ex)
        {
            this.logger.ClientError(
                nameof(this.GetScheduleAsync),
                scheduleId,
                ex);

            throw;
        }
    }

    /// <inheritdoc/>
    public override ScheduleClient GetScheduleClient(string scheduleId)
    {
        Check.NotNullOrEmpty(scheduleId, nameof(scheduleId));
        return new ScheduleClientImpl(this.durableTaskClient, scheduleId, this.logger);
    }

    /// <inheritdoc/>
    public override AsyncPageable<ScheduleDescription> ListSchedulesAsync(ScheduleQuery? filter = null)
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
                        {
                            // If there's no filter, return all items
                            if (filter == null)
                            {
                                return true;
                            }

                            // Check status filter if specified
                            bool statusMatches = !filter.Status.HasValue || metadata.State.Status == filter.Status.Value;

                            // Check created from date filter if specified
                            bool createdFromMatches = !filter.CreatedFrom.HasValue || metadata.State.ScheduleCreatedAt > filter.CreatedFrom.Value;

                            // Check created to date filter if specified
                            bool createdToMatches = !filter.CreatedTo.HasValue || metadata.State.ScheduleCreatedAt < filter.CreatedTo.Value;

                            return statusMatches && createdFromMatches && createdToMatches;
                        })
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
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // the operation was cancelled as requested. No need to log this.
                throw;
            }
            catch (Exception ex)
            {
                this.logger.ClientError(
                    nameof(this.ListSchedulesAsync),
                    string.Empty,
                    ex);

                throw;
            }
        });
    }
}
