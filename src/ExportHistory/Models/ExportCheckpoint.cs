// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Checkpoint information used to resume export.
/// </summary>
public sealed class ExportCheckpoint
{
    /// <summary>
    /// Gets or sets the last terminal time processed.
    /// </summary>
    public DateTimeOffset? LastTerminalTimeProcessed { get; set; }

    /// <summary>
    /// Gets or sets the last instance ID processed.
    /// </summary>
    public string? LastInstanceIdProcessed { get; set; }

    /// <summary>
    /// Gets or sets the continuation token for pagination.
    /// </summary>
    public string? ContinuationToken { get; set; }
}

