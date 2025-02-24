// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Represents a handle to a scheduled task, providing operations for managing the schedule.
/// </summary>
class ScheduleHandle : IScheduleHandle
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
    EntityInstanceId EntityId { get; }

    /// <inheritdoc/>
    public async Task<ScheduleDescription> DescribeAsync(CancellationToken cancellation = default)
    {
        try
        {
            Check.NotNullOrEmpty(this.ScheduleId, nameof(this.ScheduleId));

            EntityInstanceId entityId = new EntityInstanceId(nameof(Schedule), this.ScheduleId);
            EntityMetadata<ScheduleState>? metadata =
                await this.durableTaskClient.Entities.GetEntityAsync<ScheduleState>(entityId, cancellation: cancellation);
            if (metadata == null)
            {
                throw new ScheduleNotFoundException(this.ScheduleId);
            }

            ScheduleState state = metadata.State;

            ScheduleConfiguration? config = state.ScheduleConfiguration;

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
            };
        }
        catch (OperationCanceledException ex)
        {
            this.logger.ClientError(
                nameof(this.DescribeAsync),
                this.ScheduleId,
                ex);

            throw new OperationCanceledException(
                $"The {nameof(this.DescribeAsync)} operation was canceled.",
                ex,
                cancellation);
        }
    }

    /// <inheritdoc/>
    public async Task PauseAsync(CancellationToken cancellation = default)
    {
        try
        {
            this.logger.ClientPausingSchedule(this.ScheduleId);

            ScheduleOperationRequest request = new ScheduleOperationRequest(this.EntityId, nameof(Schedule.PauseSchedule));
            string instanceId = await this.durableTaskClient.ScheduleNewOrchestrationInstanceAsync(
                new TaskName(nameof(ExecuteScheduleOperationOrchestrator)),
                request,
                cancellation);

            // Wait for the orchestration to complete
            OrchestrationMetadata state = await this.durableTaskClient.WaitForInstanceCompletionAsync(instanceId, true, cancellation);

            if (state.RuntimeStatus != OrchestrationRuntimeStatus.Completed)
            {
                throw new InvalidOperationException($"Failed to pause schedule '{this.ScheduleId}': {state.FailureDetails?.ErrorMessage ?? string.Empty}");
            }
        }
        catch (OperationCanceledException ex)
        {
            this.logger.ClientError(
                nameof(this.PauseAsync),
                this.ScheduleId,
                ex);

            throw new OperationCanceledException(
                $"The {nameof(this.PauseAsync)} operation was canceled.",
                null,
                cancellation);
        }
    }

    /// <inheritdoc/>
    public async Task ResumeAsync(CancellationToken cancellation = default)
    {
        try
        {
            this.logger.ClientResumingSchedule(this.ScheduleId);

            ScheduleOperationRequest request = new ScheduleOperationRequest(this.EntityId, nameof(Schedule.ResumeSchedule));
            string instanceId = await this.durableTaskClient.ScheduleNewOrchestrationInstanceAsync(
                new TaskName(nameof(ExecuteScheduleOperationOrchestrator)),
                request,
                cancellation);

            // Wait for the orchestration to complete
            OrchestrationMetadata state = await this.durableTaskClient.WaitForInstanceCompletionAsync(instanceId, true, cancellation);

            if (state.RuntimeStatus != OrchestrationRuntimeStatus.Completed)
            {
                throw new InvalidOperationException($"Failed to resume schedule '{this.ScheduleId}': {state.FailureDetails?.ErrorMessage ?? string.Empty}");
            }
        }
        catch (OperationCanceledException ex)
        {
            this.logger.ClientError(
                nameof(this.ResumeAsync),
                this.ScheduleId,
                ex);

            throw new OperationCanceledException(
                $"The {nameof(this.ResumeAsync)} operation was canceled.",
                null,
                cancellation);
        }
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(ScheduleUpdateOptions updateOptions, CancellationToken cancellation = default)
    {
        try
        {
            Check.NotNull(updateOptions, nameof(updateOptions));
            this.logger.ClientUpdatingSchedule(this.ScheduleId);

            ScheduleOperationRequest request = new ScheduleOperationRequest(this.EntityId, nameof(Schedule.UpdateSchedule), updateOptions);
            string instanceId = await this.durableTaskClient.ScheduleNewOrchestrationInstanceAsync(
                new TaskName(nameof(ExecuteScheduleOperationOrchestrator)),
                request,
                cancellation);

            // Wait for the orchestration to complete
            OrchestrationMetadata state = await this.durableTaskClient.WaitForInstanceCompletionAsync(instanceId, true, cancellation);

            if (state.RuntimeStatus != OrchestrationRuntimeStatus.Completed)
            {
                throw new InvalidOperationException($"Failed to update schedule '{this.ScheduleId}': {state.FailureDetails?.ErrorMessage ?? string.Empty}");
            }
        }
        catch (OperationCanceledException ex)
        {
            this.logger.ClientError(
                nameof(this.UpdateAsync),
                this.ScheduleId,
                ex);

            throw new OperationCanceledException(
                $"The {nameof(this.UpdateAsync)} operation was canceled.",
                null,
                cancellation);
        }
    }

    // TODO: verify deleting non existent wont throw exception

    /// <inheritdoc/>
    public async Task DeleteAsync(CancellationToken cancellation = default)
    {
        try
        {
            this.logger.ClientDeletingSchedule(this.ScheduleId);

            ScheduleOperationRequest request = new ScheduleOperationRequest(this.EntityId, "delete");
            string instanceId = await this.durableTaskClient.ScheduleNewOrchestrationInstanceAsync(
                new TaskName(nameof(ExecuteScheduleOperationOrchestrator)),
                request,
                cancellation);

            // Wait for the orchestration to complete
            OrchestrationMetadata state = await this.durableTaskClient.WaitForInstanceCompletionAsync(instanceId, true, cancellation);

            if (state.RuntimeStatus != OrchestrationRuntimeStatus.Completed)
            {
                throw new InvalidOperationException($"Failed to delete schedule '{this.ScheduleId}': {state.FailureDetails?.ErrorMessage ?? string.Empty}");
            }
        }
        catch (OperationCanceledException ex)
        {
            this.logger.ClientError(
                nameof(this.DeleteAsync),
                this.ScheduleId,
                ex);

            throw new OperationCanceledException(
                $"The {nameof(this.DeleteAsync)} operation was canceled.",
                null,
                cancellation);
        }
    }
}
