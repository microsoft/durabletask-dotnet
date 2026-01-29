// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.ExportHistory;

namespace ExportHistoryWebApp.Models;

/// <summary>
/// Represents a request to create a new export job.
/// </summary>
public class CreateExportJobRequest
{
    /// <summary>
    /// Gets or sets the unique identifier for the export job. If not provided, a GUID will be generated.
    /// </summary>
    public string? JobId { get; set; }

    /// <summary>
    /// Gets or sets the export mode (Batch or Continuous).
    /// </summary>
    public ExportMode Mode { get; set; }

    /// <summary>
    /// Gets or sets the start time for the export based on completion time (inclusive). Required.
    /// </summary>
    public DateTimeOffset CompletedTimeFrom { get; set; }

    /// <summary>
    /// Gets or sets the end time for the export based on completion time (inclusive). Required for Batch mode, null for Continuous mode.
    /// </summary>
    public DateTimeOffset? CompletedTimeTo { get; set; }

    /// <summary>
    /// Gets or sets the blob container name where exported data will be stored. Optional if default storage is configured.
    /// </summary>
    public string? Container { get; set; }

    /// <summary>
    /// Gets or sets an optional prefix for blob paths.
    /// </summary>
    public string? Prefix { get; set; }

    /// <summary>
    /// Gets or sets the export format settings. Optional, defaults to jsonl-gzip.
    /// </summary>
    public ExportFormat? Format { get; set; }

    /// <summary>
    /// Gets or sets the orchestration runtime statuses to filter by. Optional.
    /// Valid statuses are: Completed, Failed, Terminated.
    /// </summary>
    public List<OrchestrationRuntimeStatus>? RuntimeStatus { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of instances to fetch per batch. Optional, defaults to 100.
    /// </summary>
    public int? MaxInstancesPerBatch { get; set; }
}

