// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Convenience client for managing export jobs via entity signals and reads.
/// </summary>
public sealed class ExportHistoryJobClient
{
    public readonly string JobId;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExportJobClient"/> class.
    /// </summary>
    public ExportHistoryJobClient(string jobId)
    {
        this.JobId = Check.NotNullOrEmpty(jobId, nameof(jobId));
    }

    public Task CreateAsync(ExportJobCreationOptions options, CancellationToken cancellation = default)
    public Task UpdateAsync(ExportJobUpdateOptions options, CancellationToken cancellation = default)
    public Task PauseAsync(CancellationToken cancellation = default)
    public Task ResumeAsync(CancellationToken cancellation = default)
    public Task DeleteAsync(CancellationToken cancellation = default)
}


