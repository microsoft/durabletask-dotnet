// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Convenience client for managing export jobs via entity signals and reads.
/// </summary>
public abstract class ExportHistoryJobClient
{
    public readonly string JobId;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExportJobClient"/> class.
    /// </summary>
    protected ExportHistoryJobClient(string jobId)
    {
        this.JobId = Check.NotNullOrEmpty(jobId, nameof(jobId));
    }

    public abstract Task CreateAsync(ExportJobCreationOptions options, CancellationToken cancellation = default);
    public abstract Task<ExportJobDescription> DescribeAsync(CancellationToken cancellation = default);
    public abstract Task DeleteAsync(CancellationToken cancellation = default);
}


