// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Convenience client for managing export jobs via entity signals and reads.
/// </summary>
public abstract class ExportHistoryClient
{
    /// <summary>
    /// Creates a new export job.
    /// </summary>
    /// <param name="options">The options for the export job.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public abstract Task<ExportHistoryJobClient> CreateJobAsync(ExportJobCreationOptions options, CancellationToken cancellation = default);

    /// <summary>
    /// Gets an export job.
    /// </summary>
    /// <param name="jobId">The ID of the export job.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public abstract Task<ExportJobDescription> GetJobAsync(string jobId, CancellationToken cancellation = default);

    /// <summary>
    /// Lists export jobs.
    /// </summary>
    /// <param name="filter">The filter for the export jobs.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public abstract AsyncPageable<ExportJobDescription> ListJobsAsync(ExportJobQuery? filter = null);

    /// <summary>
    /// Gets an export job client.
    /// </summary>
    /// <param name="jobId">The ID of the export job.</param>
    /// <returns>The export job client.</returns>
    public abstract ExportHistoryJobClient GetJobClient(string jobId);
}
