// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Exception thrown when attempting to access an export job that does not exist.
/// </summary>
public class ExportJobNotFoundException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExportJobNotFoundException"/> class.
    /// </summary>
    /// <param name="jobId">The ID of the export history job that was not found.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ExportJobNotFoundException(string jobId, Exception? innerException = null)
        : base($"Export history job with ID '{jobId}' was not found.", innerException!)
    {
        this.JobId = jobId;
    }

    /// <summary>
    /// Gets the ID of the export history job that was not found.
    /// </summary>
    public string JobId { get; }
}
