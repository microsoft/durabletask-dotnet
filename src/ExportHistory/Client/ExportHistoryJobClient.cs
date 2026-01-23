// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Convenience client for managing export jobs via entity signals and reads.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ExportHistoryJobClient"/> class.
/// </remarks>
public abstract class ExportHistoryJobClient(string jobId)
{
    /// <summary>
    /// The ID of the export job.
    /// </summary>
    protected readonly string JobId = Check.NotNullOrEmpty(jobId, nameof(jobId));

    /// <summary>
    /// Creates a new export job.
    /// </summary>
    /// <param name="options">The options for the export job.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public abstract Task CreateAsync(ExportJobCreationOptions options, CancellationToken cancellation = default);

    /// <summary>
    /// Describes the export job.
    /// </summary>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public abstract Task<ExportJobDescription> DescribeAsync(CancellationToken cancellation = default);

    /// <summary>
    /// Deletes the export job.
    /// </summary>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public abstract Task DeleteAsync(CancellationToken cancellation = default);
}
