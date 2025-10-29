// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Convenience client for managing export jobs via entity signals and reads.
/// </summary>
public sealed class DefaultExportHistoryJobClient(
    DurableTaskClient durableTaskClient,
    string jobId,
    ILogger logger,
    ExportHistoryStorageOptions storageOptions
) : ExportHistoryJobClient(jobId)
{
    readonly DurableTaskClient durableTaskClient = Check.NotNull(durableTaskClient, nameof(durableTaskClient));
    readonly ILogger logger = Check.NotNull(logger, nameof(logger));
    readonly ExportHistoryStorageOptions storageOptions = Check.NotNull(storageOptions, nameof(storageOptions));
    readonly EntityInstanceId entityId = new(nameof(ExportJob), jobId);

    public override async Task CreateAsync(ExportJobCreationOptions options, CancellationToken cancellation = default)
    {
        try
        {
            Check.NotNull(options, nameof(options));

            // If destination is not provided, construct it from storage options
            ExportJobCreationOptions optionsWithDestination = options;
            if (options.Destination == null)
            {
                ExportDestination destination = new ExportDestination(this.storageOptions.ContainerName)
                {
                    Prefix = this.storageOptions.Prefix,
                };

                optionsWithDestination = options with { Destination = destination };
            }

            ExportJobOperationRequest request = 
                new ExportJobOperationRequest(
                    this.entityId, 
                    nameof(ExportJob.Create), 
                    optionsWithDestination);

            string instanceId = await this.durableTaskClient
                .ScheduleNewOrchestrationInstanceAsync(
                    new TaskName(nameof(ExecuteExportJobOperationOrchestrator)),
                    request,
                    cancellation);

            // Wait for the orchestration to complete
            OrchestrationMetadata state = await this.durableTaskClient
                .WaitForInstanceCompletionAsync(
                    instanceId, 
                    true, 
                    cancellation);

            if (state.RuntimeStatus != OrchestrationRuntimeStatus.Completed)
            {
                throw new InvalidOperationException(
                    $"Failed to create export job '{this.JobId}': " +
                    $"{state.FailureDetails?.ErrorMessage ?? string.Empty}");
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // the operation was cancelled as requested. No need to log this.
            throw;
        }
        catch (Exception ex)
        {
            this.logger.ClientError(nameof(this.CreateAsync), this.JobId, ex);

            throw;
        }
    }

    // TODO: there is no atomicity guarantee of deleting entity and purging the orchestrator
    // Add sweeping process to clean up orphaned orchestrations failed to be purged
    public override async Task DeleteAsync(CancellationToken cancellation = default)
    {
        try
        {
            this.logger.ClientDeletingExportJob(this.JobId);

            string orchestrationInstanceId = ExportHistoryConstants.GetOrchestratorInstanceId(this.JobId);

            // First, delete the entity
            ExportJobOperationRequest request = new ExportJobOperationRequest(this.entityId, ExportJobOperations.Delete);
            string instanceId = await this.durableTaskClient.ScheduleNewOrchestrationInstanceAsync(
                new TaskName(nameof(ExecuteExportJobOperationOrchestrator)),
                request,
                cancellation);

            // Wait for the orchestration to complete
            OrchestrationMetadata state = await this.durableTaskClient.WaitForInstanceCompletionAsync(instanceId, true, cancellation);

            if (state.RuntimeStatus != OrchestrationRuntimeStatus.Completed)
            {
                throw new InvalidOperationException($"Failed to delete export job '{this.JobId}': {state.FailureDetails?.ErrorMessage ?? string.Empty}");
            }

            // Then terminate the linked export orchestration if it exists
            await this.TerminateAndPurgeOrchestrationAsync(orchestrationInstanceId, cancellation);

            // Verify both entity and orchestration are gone
            await this.VerifyDeletionAsync(orchestrationInstanceId, cancellation);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // the operation was cancelled as requested. No need to log this.
            throw;
        }
        catch (Exception ex)
        {
            this.logger.ClientError(nameof(this.DeleteAsync), this.JobId, ex);

            throw;
        }
    }

    public override async Task<ExportJobDescription> DescribeAsync(CancellationToken cancellation = default)
    {
        try
        {
            Check.NotNullOrEmpty(this.JobId, nameof(this.JobId));

            EntityMetadata<ExportJobState>? metadata =
                await this.durableTaskClient.Entities.GetEntityAsync<ExportJobState>(this.entityId, cancellation: cancellation);
            if (metadata == null)
            {
                throw new ExportJobNotFoundException(this.JobId);
            }

            ExportJobState state = metadata.State;

            ExportJobConfiguration? config = state.Config;

            // Determine if the export orchestrator instance exists and capture its instance ID if so
            string orchestratorInstanceId = ExportHistoryConstants.GetOrchestratorInstanceId(this.JobId);
            OrchestrationMetadata? orchestratorState = await this.durableTaskClient.GetInstanceAsync(orchestratorInstanceId, cancellation: cancellation);
            string? presentOrchestratorId = orchestratorState != null ? orchestratorInstanceId : null;

            return new ExportJobDescription
            {
                JobId = this.JobId,
                Status = state.Status,
                CreatedAt = state.CreatedAt,
                LastModifiedAt = state.LastModifiedAt,
                Config = config,
                OrchestratorInstanceId = presentOrchestratorId,
            };
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // the operation was cancelled as requested. No need to log this.
            throw;
        }
        catch (Exception ex)
        {
            this.logger.ClientError(nameof(this.DescribeAsync), this.JobId, ex);

            throw;
        }
    }

    /// <summary>
    /// Terminates and purges the export orchestration instance.
    /// </summary>
    /// <param name="orchestrationInstanceId">The orchestration instance ID to terminate and purge.</param>
    /// <param name="cancellation">The cancellation token.</param>
    async Task TerminateAndPurgeOrchestrationAsync(string orchestrationInstanceId, CancellationToken cancellation)
    {
        try
        {
            // Terminate the orchestration (will fail silently if it doesn't exist or already terminated)
            await this.durableTaskClient.TerminateInstanceAsync(
                orchestrationInstanceId,
                new TerminateInstanceOptions { Output = "Export job deleted" },
                cancellation);

            // Wait for the orchestration to be terminated before purging
            OrchestrationMetadata? orchestrationState = await this.WaitForOrchestrationTerminationAsync(
                orchestrationInstanceId,
                cancellation);

            // Purge the orchestration instance after it's terminated
            if (orchestrationState != null && DefaultExportHistoryJobClient.IsTerminalStatus(orchestrationState.RuntimeStatus))
            {
                await this.durableTaskClient.PurgeInstanceAsync(
                    orchestrationInstanceId,
                    cancellation: cancellation);
            }
            else if (orchestrationState != null)
            {
                throw new InvalidOperationException(
                    $"Failed to delete export job '{this.JobId}': Cannot purge orchestration '{orchestrationInstanceId}' because it is still in '{orchestrationState.RuntimeStatus}' status.");
            }
        }
        catch (Exception ex) when (!(ex is InvalidOperationException))
        {
            // Log but don't fail if termination fails (orchestration may not exist or already be terminated)
            this.logger.ClientError(
                $"Failed to terminate or purge linked orchestration '{orchestrationInstanceId}': {ex.Message}",
                this.JobId,
                ex);
            // Continue to verification - if orchestration doesn't exist, verification will pass
        }
    }

    /// <summary>
    /// Waits for an orchestration to reach a terminal state.
    /// </summary>
    /// <param name="orchestrationInstanceId">The orchestration instance ID.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>The orchestration metadata, or null if the orchestration doesn't exist.</returns>
    async Task<OrchestrationMetadata?> WaitForOrchestrationTerminationAsync(
        string orchestrationInstanceId,
        CancellationToken cancellation)
    {
        OrchestrationMetadata? orchestrationState = null;
        int waitAttempt = 0;

        while (waitAttempt < ExportHistoryConstants.MaxTerminationWaitAttempts)
        {
            orchestrationState = await this.durableTaskClient.GetInstanceAsync(
                orchestrationInstanceId,
                cancellation: cancellation);

            if (orchestrationState == null || DefaultExportHistoryJobClient.IsTerminalStatus(orchestrationState.RuntimeStatus))
            {
                break;
            }

            // Wait a bit before checking again
            await Task.Delay(
                TimeSpan.FromMilliseconds(ExportHistoryConstants.TerminationWaitDelayMs),
                cancellation);
            waitAttempt++;
        }

        return orchestrationState;
    }

    /// <summary>
    /// Checks if an orchestration runtime status is a terminal state.
    /// </summary>
    /// <param name="runtimeStatus">The runtime status to check.</param>
    /// <returns>True if the status is terminal; otherwise, false.</returns>
    static bool IsTerminalStatus(OrchestrationRuntimeStatus runtimeStatus)
    {
        return runtimeStatus == OrchestrationRuntimeStatus.Terminated ||
               runtimeStatus == OrchestrationRuntimeStatus.Completed ||
               runtimeStatus == OrchestrationRuntimeStatus.Failed;
    }

    /// <summary>
    /// Verifies that both the entity and orchestration have been deleted.
    /// </summary>
    /// <param name="orchestrationInstanceId">The orchestration instance ID to verify.</param>
    /// <param name="cancellation">The cancellation token.</param>
    async Task VerifyDeletionAsync(string orchestrationInstanceId, CancellationToken cancellation)
    {
        List<string> stillExist = new();

        // Check if entity still exists
        EntityMetadata<ExportJobState>? entityMetadata = await this.durableTaskClient.Entities.GetEntityAsync<ExportJobState>(
            this.entityId,
            cancellation: cancellation);
        if (entityMetadata != null)
        {
            stillExist.Add($"entity '{this.entityId}'");
        }

        // Check if orchestration still exists
        OrchestrationMetadata? orchestrationMetadata = await this.durableTaskClient.GetInstanceAsync(
            orchestrationInstanceId,
            cancellation: cancellation);
        if (orchestrationMetadata != null)
        {
            stillExist.Add($"orchestration '{orchestrationInstanceId}'");
        }

        // Throw exception if either still exists
        if (stillExist.Count > 0)
        {
            string items = string.Join(" and ", stillExist);
            throw new InvalidOperationException(
                $"Failed to delete export job '{this.JobId}': The following resources still exist: {items}.");
        }
    }
}


