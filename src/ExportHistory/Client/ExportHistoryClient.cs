// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Convenience client for managing export jobs via entity signals and reads.
/// </summary>
public abstract class ExportHistoryClient
{
    public abstract Task<ExportHistoryJobClient> CreateJobAsync(ExportJobCreationOptions options, CancellationToken cancellation = default);
    public abstract Task<ExportJobDescription> GetJobAsync(string jobId, CancellationToken cancellation = default);

    public abstract AsyncPageable<ExportJobDescription> ListJobsAsync(ExportJobQuery? filter = null);
    public abstract ExportHistoryJobClient GetJobClient(string jobId);
}