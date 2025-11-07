// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Convenience client for managing export jobs via entity signals and reads.
/// </summary>
public sealed class DefaultExportHistoryClient(
    DurableTaskClient durableTaskClient,
    ILogger<DefaultExportHistoryClient> logger,
    ExportHistoryStorageOptions storageOptions) : ExportHistoryClient
{
    readonly DurableTaskClient durableTaskClient = Check.NotNull(durableTaskClient, nameof(durableTaskClient));
    readonly ILogger<DefaultExportHistoryClient> logger = Check.NotNull(logger, nameof(logger));
    readonly ExportHistoryStorageOptions storageOptions = Check.NotNull(storageOptions, nameof(storageOptions));

    /// <inheritdoc/>
    public override async Task<ExportHistoryJobClient> CreateJobAsync(
        ExportJobCreationOptions options,
        CancellationToken cancellation = default)
    {
        Check.NotNull(options, nameof(options));
        this.logger.ClientCreatingExportJob(options);

        try
        {
            // Create export job client instance
            ExportHistoryJobClient exportHistoryJobClient = new DefaultExportHistoryJobClient(
                this.durableTaskClient,
                options.JobId,
                this.logger,
                this.storageOptions);

            // Create the export job using the client (validation already done in constructor)
            await exportHistoryJobClient.CreateAsync(options, cancellation);

            // Return the job client
            return exportHistoryJobClient;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // the operation was cancelled as requested. No need to log this.
            throw;
        }
        catch (Exception ex)
        {
            this.logger.ClientError(nameof(this.CreateJobAsync), options.JobId, ex);

            throw;
        }
    }

    /// <inheritdoc/>
    public override async Task<ExportJobDescription> GetJobAsync(string jobId, CancellationToken cancellation = default)
    {
        Check.NotNullOrEmpty(jobId, nameof(jobId));

        try
        {
            // Get export history job client first
            ExportHistoryJobClient exportHistoryJobClient = this.GetJobClient(jobId);

            // Call DescribeAsync which handles all the entity state mapping
            return await exportHistoryJobClient.DescribeAsync(cancellation);
        }
        catch (ExportJobNotFoundException)
        {
            // Re-throw as the job not being found is an error condition
            throw;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // the operation was cancelled as requested. No need to log this.
            throw;
        }
        catch (Exception ex)
        {
            this.logger.ClientError(nameof(this.GetJobAsync), jobId, ex);

            throw;
        }
    }

    /// <inheritdoc/>
    public override AsyncPageable<ExportJobDescription> ListJobsAsync(ExportJobQuery? filter = null)
    {
        // TODO: revisit the fields
        // Create an async pageable using the Pageable.Create helper
        return Pageable.Create(async (continuationToken, pageSize, cancellation) =>
        {
            try
            {
                EntityQuery query = new EntityQuery
                {
                    InstanceIdStartsWith = $"@{nameof(ExportJob)}@{filter?.JobIdPrefix ?? string.Empty}",
                    IncludeState = true,
                    PageSize = filter?.PageSize ?? ExportJobQuery.DefaultPageSize,
                    ContinuationToken = continuationToken,
                };

                // Get one page of entities
                IAsyncEnumerable<Page<EntityMetadata<ExportJobState>>> entityPages =
                    this.durableTaskClient.Entities.GetAllEntitiesAsync<ExportJobState>(query).AsPages();

                await foreach (Page<EntityMetadata<ExportJobState>> entityPage in entityPages)
                {
                    List<ExportJobDescription> exportJobs = new();

                    foreach (EntityMetadata<ExportJobState> metadata in entityPage.Values)
                    {
                        if (filter != null && !MatchesFilter(metadata.State, filter))
                        {
                            continue;
                        }

                        ExportJobState state = metadata.State;
                        ExportJobConfiguration? config = state.Config;

                        exportJobs.Add(new ExportJobDescription
                        {
                            JobId = metadata.Id.Key,
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
                        });
                    }

                    return new Page<ExportJobDescription>(exportJobs, entityPage.ContinuationToken);
                }

                // Return empty page if no results
                return new Page<ExportJobDescription>(new List<ExportJobDescription>(), null);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // the operation was cancelled as requested. No need to log this.
                throw;
            }
            catch (Exception ex)
            {
                this.logger.ClientError(nameof(this.ListJobsAsync), string.Empty, ex);

                throw;
            }
        });
    }

    /// <summary>
    /// Gets a job client for the specified job ID.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <returns>The export history job client.</returns>
    public override ExportHistoryJobClient GetJobClient(string jobId)
    {
        return new DefaultExportHistoryJobClient(
            this.durableTaskClient,
            jobId,
            this.logger,
            this.storageOptions);
    }

    /// <summary>
    /// Checks if an export job state matches the provided filter criteria.
    /// </summary>
    /// <param name="state">The export job state to check.</param>
    /// <param name="filter">The filter criteria.</param>
    /// <returns>True if the state matches the filter; otherwise, false.</returns>
    static bool MatchesFilter(ExportJobState state, ExportJobQuery filter)
    {
        bool statusMatches = !filter.Status.HasValue || state.Status == filter.Status.Value;
        bool createdFromMatches = !filter.CreatedFrom.HasValue ||
            (state.CreatedAt.HasValue && state.CreatedAt.Value > filter.CreatedFrom.Value);
        bool createdToMatches = !filter.CreatedTo.HasValue ||
            (state.CreatedAt.HasValue && state.CreatedAt.Value < filter.CreatedTo.Value);

        return statusMatches && createdFromMatches && createdToMatches;
    }
}
