// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Configuration for a export job.
/// </summary>
public record ExportJobCreationOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExportJobCreationOptions"/> class.
    /// </summary>
    /// <param name="mode">The export mode (Batch or Continuous).</param>
    /// <param name="createdTimeFrom">The start time for the export (inclusive). Required.</param>
    /// <param name="createdTimeTo">The end time for the export (inclusive). Required for Batch mode, null for Continuous mode.</param>
    /// <param name="destination">The export destination where exported data will be stored. Required unless default storage is configured.</param>
    /// <param name="jobId">The unique identifier for the export job. If not provided, a GUID will be generated.</param>
    /// <param name="format">The export format settings. Optional, defaults to jsonl-gzip.</param>
    /// <param name="runtimeStatus">The orchestration runtime statuses to filter by. Optional.</param>
    /// <param name="maxInstancesPerBatch">The maximum number of instances to fetch per batch. Optional, defaults to 100.</param>
    /// <exception cref="ArgumentException">Thrown when validation fails.</exception>
    public ExportJobCreationOptions(
        ExportMode mode,
        DateTimeOffset createdTimeFrom,
        DateTimeOffset? createdTimeTo,
        ExportDestination? destination,
        string? jobId = null,
        ExportFormat? format = null,
        List<OrchestrationRuntimeStatus>? runtimeStatus = null,
        int? maxInstancesPerBatch = null)
    {
        // Generate GUID if jobId not provided
        this.JobId = string.IsNullOrEmpty(jobId) ? Guid.NewGuid().ToString("N") : jobId;

        if (mode == ExportMode.Batch && !createdTimeTo.HasValue)
        {
            throw new ArgumentException(
                "CreatedTimeTo is required for Batch export mode.",
                nameof(createdTimeTo));
        }

        if (mode == ExportMode.Batch && createdTimeTo.HasValue && createdTimeTo.Value <= createdTimeFrom)
        {
            throw new ArgumentException(
                $"CreatedTimeTo({createdTimeTo.Value}) must be greater than CreatedTimeFrom({createdTimeFrom}) for Batch export mode.",
                nameof(createdTimeTo));
        }

        if (mode == ExportMode.Continuous && createdTimeTo.HasValue)
        {
            throw new ArgumentException(
                "CreatedTimeTo must be null for Continuous export mode.",
                nameof(createdTimeTo));
        }

        // Validate maxInstancesPerBatch range if provided (must be 1..999)
        if (maxInstancesPerBatch.HasValue && (maxInstancesPerBatch.Value <= 0 || maxInstancesPerBatch.Value >= 1001))
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxInstancesPerBatch),
                maxInstancesPerBatch,
                "MaxInstancesPerBatch must be between 1 and 1000.");
        }

        // Validate terminal status-only filter here if provided
        if (runtimeStatus?.Any() == true
            && runtimeStatus.Any(
                s => s is not (OrchestrationRuntimeStatus.Completed
                               or OrchestrationRuntimeStatus.Failed
                               or OrchestrationRuntimeStatus.Terminated
                               or OrchestrationRuntimeStatus.ContinuedAsNew)))
        {
            throw new ArgumentException(
                "Export supports terminal orchestration statuses only. Valid statuses are: Completed, Failed, Terminated, and ContinuedAsNew.",
                nameof(runtimeStatus));
        }

        this.Mode = mode;
        this.CreatedTimeFrom = createdTimeFrom;
        this.CreatedTimeTo = createdTimeTo;
        this.Destination = destination;
        this.Format = format ?? ExportFormat.Default;
        this.RuntimeStatus = (runtimeStatus is { Count: > 0 })
            ? runtimeStatus
            : new List<OrchestrationRuntimeStatus>
            {
                OrchestrationRuntimeStatus.Completed,
                OrchestrationRuntimeStatus.Failed,
                OrchestrationRuntimeStatus.Terminated,
                OrchestrationRuntimeStatus.ContinuedAsNew
            };
        this.MaxInstancesPerBatch = maxInstancesPerBatch ?? 100;

    /// <summary>
    /// Gets the unique identifier for the export job.
    /// </summary>
    public string JobId { get; init; }

    /// <summary>
    /// Gets the export mode (Batch or Continuous).
    /// </summary>
    public ExportMode Mode { get; init; }

    /// <summary>
    /// Gets the start time for the export (inclusive). Required.
    /// </summary>
    public DateTimeOffset CreatedTimeFrom { get; init; }

    /// <summary>
    /// Gets the end time for the export (inclusive). Required for Batch mode, null for Continuous mode.
    /// </summary>
    public DateTimeOffset? CreatedTimeTo { get; init; }

    /// <summary>
    /// Gets the export destination where exported data will be stored. Optional.
    /// </summary>
    public ExportDestination? Destination { get; init; }

    /// <summary>
    /// Gets the export format settings.
    /// </summary>
    public ExportFormat Format { get; init; }

    /// <summary>
    /// Gets the orchestration runtime statuses to filter by.
    /// If not specified, all terminal statuses are exported.
    /// </summary>
    public List<OrchestrationRuntimeStatus>? RuntimeStatus { get; init; }

    /// <summary>
    /// Gets the maximum number of instances to fetch per batch.
    /// Defaults to 100.
    /// </summary>
    public int MaxInstancesPerBatch { get; init; }
}
