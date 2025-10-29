// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Represents the comprehensive details of an export job.
/// </summary>
public record ExportJobDescription
{
    /// <summary>
    /// Gets or sets the job identifier.
    /// </summary>
    public string JobId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the export job status.
    /// </summary>
    public ExportJobStatus Status { get; init; }

    /// <summary>
    /// Gets or sets the time when this export job was created.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// Gets or sets the time when this export job was last modified.
    /// </summary>
    public DateTimeOffset? LastModifiedAt { get; init; }

    /// <summary>
    /// Gets or sets the export job configuration.
    /// </summary>
    public ExportJobConfiguration? Config { get; init; }

    /// <summary>
    /// Gets or sets the instance ID of the running export orchestrator, if any.
    /// </summary>
    public string? OrchestratorInstanceId { get; init; }
}
