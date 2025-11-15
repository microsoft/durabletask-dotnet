// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Export job state stored in the entity.
/// </summary>
public sealed class ExportJobState
{
    /// <summary>
    /// Gets or sets the current status of the export job.
    /// </summary>
    public ExportJobStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the export job configuration.
    /// </summary>
    public ExportJobConfiguration? Config { get; set; }

    /// <summary>
    /// Gets or sets the checkpoint for resuming the export.
    /// </summary>
    public ExportCheckpoint? Checkpoint { get; set; }

    /// <summary>
    /// Gets or sets the time when the export job was created.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the time when the export job was last modified.
    /// </summary>
    public DateTimeOffset? LastModifiedAt { get; set; }

    /// <summary>
    /// Gets or sets the time of the last checkpoint.
    /// </summary>
    public DateTimeOffset? LastCheckpointTime { get; set; }

    /// <summary>
    /// Gets or sets the last error message, if any.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Gets or sets the total number of instances scanned.
    /// </summary>
    public long ScannedInstances { get; set; }

    /// <summary>
    /// Gets or sets the total number of instances exported.
    /// </summary>
    public long ExportedInstances { get; set; }

    /// <summary>
    /// Gets or sets the instance ID of the orchestrator running this export job, if any.
    /// </summary>
    public string? OrchestratorInstanceId { get; set; }
}
