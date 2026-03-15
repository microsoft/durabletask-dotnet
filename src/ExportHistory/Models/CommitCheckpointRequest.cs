// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Request to commit a checkpoint with progress updates and failures.
/// </summary>
public sealed class CommitCheckpointRequest
{
    /// <summary>
    /// Gets or sets the number of instances scanned in this batch.
    /// </summary>
    public long ScannedInstances { get; set; }

    /// <summary>
    /// Gets or sets the number of instances successfully exported in this batch.
    /// </summary>
    public long ExportedInstances { get; set; }

    /// <summary>
    /// Gets or sets the checkpoint to commit. If not null, the checkpoint is updated (cursor moves forward).
    /// If null, the current checkpoint is kept (cursor does not move forward), allowing retry of the same batch.
    /// </summary>
    public ExportCheckpoint? Checkpoint { get; set; }

    /// <summary>
    /// Gets or sets the list of failed instance exports, if any.
    /// </summary>
    public List<ExportFailure>? Failures { get; set; }
}
