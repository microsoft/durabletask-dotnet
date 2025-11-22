// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Exception thrown when client-side validation fails for export job operations.
/// </summary>
public class ExportJobClientValidationException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExportJobClientValidationException"/> class.
    /// </summary>
    /// <param name="jobId">The ID of the export job that failed validation.</param>
    /// <param name="message">The validation error message.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ExportJobClientValidationException(string jobId, string message, Exception? innerException = null)
        : base($"Validation failed for export job '{jobId}': {message}", innerException!)
    {
        this.JobId = jobId;
    }

    /// <summary>
    /// Gets the ID of the export job that failed validation.
    /// </summary>
    public string JobId { get; }
}
