// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Checkpoint information used to resume export.
/// </summary>
public sealed record ExportCheckpoint(string? LastInstanceKey = null)
{
    /// <summary>
    /// Gets the inclusive lower completion-time bound for continuous export.
    /// A null value identifies a legacy instance-key-only checkpoint.
    /// </summary>
    public DateTimeOffset? CompletedTimeFrom { get; init; }

    /// <summary>
    /// Gets the fixed inclusive upper completion-time bound while paging a continuous export window.
    /// A null value indicates that the next window has not started.
    /// </summary>
    public DateTimeOffset? CompletedTimeTo { get; init; }
}
