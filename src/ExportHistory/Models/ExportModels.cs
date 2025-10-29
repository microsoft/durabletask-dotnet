// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Export job modes.
/// </summary>
public enum ExportMode
{
    /// <summary>Unspecified.</summary>
    Unspecified = 0,
    /// <summary>Exports a fixed window and completes.</summary>
    Batch = 1,
    /// <summary>Tails terminal instances continuously.</summary>
    Continuous = 2,
}

/// <summary>
/// Export job lifecycle status.
/// </summary>
public enum ExportJobStatus
{
    /// <summary>Initial state.</summary>
    Uninitialized = 0,
    /// <summary>Actively exporting.</summary>
    Running = 1,
    /// <summary>Paused by user.</summary>
    Paused = 2,
    /// <summary>Completed (batch) with no pending failures.</summary>
    Completed = 3,
    /// <summary>Failed.</summary>
    Failed = 4,
    /// <summary>Deleting.</summary>
    Deleting = 5,
}

/// <summary>
/// Blob destination settings.
/// </summary>
public sealed class BlobDestination
{
    public string AccountUri { get; set; } = string.Empty;
    public string Container { get; set; } = string.Empty;
    public string? Prefix { get; set; }
    public string? SasToken { get; set; }
    public bool UseManagedIdentity { get; set; }
}

/// <summary>
/// Export filter settings.
/// </summary>
public sealed class ExportFilter
{
    public DateTimeOffset? CreatedTimeFrom { get; set; }
    public DateTimeOffset? CreatedTimeTo { get; set; }
    public List<OrchestrationRuntimeStatus>? RuntimeStatus { get; set; }
}

/// <summary>
/// Export format settings.
/// </summary>
public sealed class ExportFormat
{
    public string Kind { get; set; } = "jsonl-gzip";
    public string SchemaVersion { get; set; } = "v1";
}

/// <summary>
/// Export configuration.
/// </summary>
public sealed class ExportJobConfig
{
    public ExportMode Mode { get; set; } = ExportMode.Batch;
    public ExportFilter? Filter { get; set; }
    public BlobDestination Destination { get; set; } = new();
    public ExportFormat Format { get; set; } = new();
    public int MaxParallelExports { get; set; } = 32;
    public int CheckpointEveryNInstances { get; set; } = 200;
    public TimeSpan? CheckpointEvery { get; set; }
}

/// <summary>
/// Watermark used to resume export.
/// </summary>
public sealed class ExportWatermark
{
    public DateTimeOffset? LastTerminalTimeProcessed { get; set; }
    public string? LastInstanceIdProcessed { get; set; }
    public string? ContinuationToken { get; set; }
}

/// <summary>
/// Export job state stored in the entity.
/// </summary>
public sealed class ExportJobState
{
    public ExportJobStatus Status { get; set; }
    public ExportJobConfig? Config { get; set; }
    public ExportWatermark? Watermark { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? LastModifiedAt { get; set; }
    public DateTimeOffset? LastCheckpointTime { get; set; }
    public string? LastError { get; set; }

    public long ScannedInstances { get; set; }
    public long ExportedInstances { get; set; }

    public Dictionary<string, ExportFailure> FailedInstances { get; set; } = new();
}

/// <summary>
/// Progress update.
/// </summary>
public sealed class ExportProgress
{
    public long ScannedInstances { get; set; }
    public long ExportedInstances { get; set; }
    public ExportWatermark? Watermark { get; set; }
}

/// <summary>
/// Failure of a specific instance export.
/// </summary>
public sealed record ExportFailure(string InstanceId, string Reason, int AttemptCount, DateTimeOffset LastAttempt);


/// <summary>
/// Orchestrator input to start a runner for a given job.
/// </summary>
public sealed record ExportJobRunRequest(EntityInstanceId JobEntityId, string ExecutionToken);


