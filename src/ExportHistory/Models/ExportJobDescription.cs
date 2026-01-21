// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Represents the comprehensive details of an export job.
/// </summary>
public record ExportJobDescription
{
    /// <summary>
    /// Gets the job identifier.
    /// </summary>
    public string JobId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the export job status.
    /// </summary>
    public ExportJobStatus Status { get; init; }

    /// <summary>
    /// Gets the time when this export job was created.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// Gets the time when this export job was last modified.
    /// </summary>
    public DateTimeOffset? LastModifiedAt { get; init; }

    /// <summary>
    /// Gets the export job configuration.
    /// </summary>
    public ExportJobConfiguration? Config { get; init; }

    /// <summary>
    /// Gets the instance ID of the running export orchestrator, if any.
    /// </summary>
    public string? OrchestratorInstanceId { get; init; }

    /// <summary>
    /// Gets the total number of instances scanned.
    /// </summary>
    public long ScannedInstances { get; init; }

    /// <summary>
    /// Gets the total number of instances exported.
    /// </summary>
    public long ExportedInstances { get; init; }

    /// <summary>
    /// Gets the last error message, if any.
    /// </summary>
    public string? LastError { get; init; }

    /// <summary>
    /// Gets the checkpoint for resuming the export.
    /// </summary>
    public ExportCheckpoint? Checkpoint { get; init; }

    /// <summary>
    /// Gets the time of the last checkpoint.
    /// </summary>
    public DateTimeOffset? LastCheckpointTime { get; init; }
}
