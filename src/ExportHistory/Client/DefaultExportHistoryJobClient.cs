// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
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

            // Determine default prefix based on mode if not already set
            string? defaultPrefix = $"{options.Mode.ToString().ToLower()}/";

            // If destination is not provided, construct it from storage options
            ExportJobCreationOptions optionsWithDestination = options;
            if (options.Destination == null)
            {
                // Use storage options prefix if provided, otherwise use mode-based default
                string? prefix = this.storageOptions.Prefix ?? defaultPrefix;

                ExportDestination destination = new ExportDestination(this.storageOptions.ContainerName)
                {
                    Prefix = prefix,
                };

                optionsWithDestination = options with { Destination = destination };
            }
            else if (string.IsNullOrEmpty(options.Destination.Prefix))
            {
                // Destination provided but no prefix set - use mode-based default
                ExportDestination destinationWithPrefix = new ExportDestination(options.Destination.Container)
                {
                    Prefix = defaultPrefix,
                };
                optionsWithDestination = options with { Destination = destinationWithPrefix };
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

            // Then terminate the linked export orchestration if it exists
            await this.TerminateAndPurgeOrchestrationAsync(orchestrationInstanceId, cancellation);
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

            return new ExportJobDescription
            {
                JobId = this.JobId,
                Status = state.Status,
                CreatedAt = state.CreatedAt,
                LastModifiedAt = state.LastModifiedAt,
                Config = config,
                OrchestratorInstanceId = state.OrchestratorInstanceId,
                ScannedInstances = state.ScannedInstances,
                ExportedInstances = state.ExportedInstances,
                LastError = state.LastError,
                Checkpoint = state.Checkpoint,
                LastCheckpointTime = state.LastCheckpointTime,
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
            await this.durableTaskClient.WaitForInstanceCompletionAsync(orchestrationInstanceId, cancellation);

            // Purge the orchestration instance after it's terminated
            await this.durableTaskClient.PurgeInstanceAsync(orchestrationInstanceId, cancellation: cancellation);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            // Orchestration instance doesn't exist - this is expected if it was already deleted or never existed
            this.logger.LogInformation(
                "Orchestration instance '{OrchestrationInstanceId}' is already purged",
                orchestrationInstanceId);
        }
        catch (Exception ex)
        {
            this.logger.ClientError(
                $"Failed to terminate or purge linked orchestration '{orchestrationInstanceId}': {ex.Message}",
                this.JobId,
                ex);
        }
    }
}


