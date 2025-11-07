// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Exception thrown when an invalid state transition is attempted on an export job.
/// </summary>
public class ExportJobInvalidTransitionException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExportJobInvalidTransitionException"/> class.
    /// </summary>
    /// <param name="jobId">The ID of the export job on which the invalid transition was attempted.</param>
    /// <param name="fromStatus">The current status of the export job.</param>
    /// <param name="toStatus">The target status that was invalid.</param>
    /// <param name="operationName">The name of the operation that was attempted.</param>
    public ExportJobInvalidTransitionException(string jobId, ExportJobStatus fromStatus, ExportJobStatus toStatus, string operationName)
        : base($"Invalid state transition attempted for export job '{jobId}': Cannot transition from {fromStatus} to {toStatus} during {operationName} operation.")
    {
        this.JobId = jobId;
        this.FromStatus = fromStatus;
        this.ToStatus = toStatus;
        this.OperationName = operationName;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExportJobInvalidTransitionException"/> class.
    /// </summary>
    /// <param name="jobId">The ID of the export job on which the invalid transition was attempted.</param>
    /// <param name="fromStatus">The current status of the export job.</param>
    /// <param name="toStatus">The target status that was invalid.</param>
    /// <param name="operationName">The name of the operation that was attempted.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ExportJobInvalidTransitionException(string jobId, ExportJobStatus fromStatus, ExportJobStatus toStatus, string operationName, Exception innerException)
        : base($"Invalid state transition attempted for export job '{jobId}': Cannot transition from {fromStatus} to {toStatus} during {operationName} operation.", innerException)
    {
        this.JobId = jobId;
        this.FromStatus = fromStatus;
        this.ToStatus = toStatus;
        this.OperationName = operationName;
    }

    /// <summary>
    /// Gets the ID of the export job that encountered the invalid transition.
    /// </summary>
    public string JobId { get; }

    /// <summary>
    /// Gets the status the export job was transitioning from.
    /// </summary>
    public ExportJobStatus FromStatus { get; }

    /// <summary>
    /// Gets the invalid target status that was attempted.
    /// </summary>
    public ExportJobStatus ToStatus { get; }

    /// <summary>
    /// Gets the name of the operation that was attempted.
    /// </summary>
    public string OperationName { get; }
}
