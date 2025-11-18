// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Microsoft.DurableTask.Client;

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Configuration for an export job.
/// </summary>
public record ExportJobCreationOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExportJobCreationOptions"/> class.
    /// </summary>
    public ExportJobCreationOptions()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExportJobCreationOptions"/> class.
    /// </summary>
    /// <param name="mode">The export mode (Batch or Continuous).</param>
    /// <param name="completedTimeFrom">The start time for the export based on completion time (inclusive). Required for Batch mode. For Continuous mode, this will be set to UtcNow if not provided.</param>
    /// <param name="completedTimeTo">The end time for the export based on completion time (inclusive). Required for Batch mode, null for Continuous mode.</param>
    /// <param name="destination">The export destination where exported data will be stored. Required unless default storage is configured.</param>
    /// <param name="jobId">The unique identifier for the export job. If not provided, a GUID will be generated.</param>
    /// <param name="format">The export format settings. Optional, defaults to jsonl-gzip.</param>
    /// <param name="runtimeStatus">The orchestration runtime statuses to filter by. Optional.</param>
    /// <param name="maxInstancesPerBatch">The maximum number of instances to fetch per batch. Optional, defaults to 100.</param>
    /// <exception cref="ArgumentException">Thrown when validation fails.</exception>
    public ExportJobCreationOptions(
        ExportMode mode,
        DateTimeOffset? completedTimeFrom,
        DateTimeOffset? completedTimeTo,
        ExportDestination? destination,
        string? jobId = null,
        ExportFormat? format = null,
        List<OrchestrationRuntimeStatus>? runtimeStatus = null,
        int? maxInstancesPerBatch = null)
    {
        // Generate GUID if jobId not provided
        this.JobId = string.IsNullOrEmpty(jobId) ? Guid.NewGuid().ToString("N") : jobId;

        if (mode == ExportMode.Batch)
        {
            if (!completedTimeFrom.HasValue)
            {
                throw new ArgumentException(
                    "CompletedTimeFrom is required for Batch export mode.",
                    nameof(completedTimeFrom));
            }

            if (!completedTimeTo.HasValue)
            {
                throw new ArgumentException(
                    "CompletedTimeTo is required for Batch export mode.",
                    nameof(completedTimeTo));
            }

            if (completedTimeTo.HasValue && completedTimeTo.Value <= completedTimeFrom)
            {
                throw new ArgumentException(
                    $"CompletedTimeTo({completedTimeTo.Value}) must be greater than CompletedTimeFrom({completedTimeFrom}) for Batch export mode.",
                    nameof(completedTimeTo));
            }

            if (completedTimeTo.HasValue && completedTimeTo.Value > DateTimeOffset.UtcNow)
            {
                throw new ArgumentException(
                    $"CompletedTimeTo({completedTimeTo.Value}) cannot be in the future. It must be less than or equal to the current time ({DateTimeOffset.UtcNow}).",
                    nameof(completedTimeTo));
            }
        }
        else if (mode == ExportMode.Continuous)
        {
            if (completedTimeTo.HasValue)
            {
                throw new ArgumentException(
                    "CompletedTimeTo is not allowed for Continuous export mode.",
                    nameof(completedTimeTo));
            }
        }
        else
        {
            throw new ArgumentException(
                "Invalid export mode.",
                nameof(mode));
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
        if (runtimeStatus is { Count: > 0 }
            && runtimeStatus.Any(
                s => s is not (OrchestrationRuntimeStatus.Completed
                               or OrchestrationRuntimeStatus.Failed
                               or OrchestrationRuntimeStatus.Terminated)))
        {
            throw new ArgumentException(
                "Export supports terminal orchestration statuses only. Valid statuses are: Completed, Failed, and Terminated.",
                nameof(runtimeStatus));
        }

        this.Mode = mode;
        this.CompletedTimeFrom = completedTimeFrom ?? DateTimeOffset.UtcNow;
        this.CompletedTimeTo = completedTimeTo;
        this.Destination = destination;
        this.Format = format ?? ExportFormat.Default;
        this.RuntimeStatus = (runtimeStatus is { Count: > 0 })
            ? runtimeStatus
            : new List<OrchestrationRuntimeStatus>
            {
                OrchestrationRuntimeStatus.Completed,
                OrchestrationRuntimeStatus.Failed,
                OrchestrationRuntimeStatus.Terminated,
            };
        this.MaxInstancesPerBatch = maxInstancesPerBatch ?? 100;
    }

    /// <summary>
    /// Gets the unique identifier for the export job.
    /// </summary>
    public string JobId { get; init; }

    /// <summary>
    /// Gets the export mode (Batch or Continuous).
    /// </summary>
    public ExportMode Mode { get; init; }

    /// <summary>
    /// Gets the start time for the export based on completion time (inclusive).
    /// Required for Batch mode. For Continuous mode, this is automatically set to UtcNow when creating the job.
    /// </summary>
    public DateTimeOffset CompletedTimeFrom { get; init; }

    /// <summary>
    /// Gets the end time for the export based on completion time (inclusive). Required for Batch mode, null for Continuous mode.
    /// </summary>
    public DateTimeOffset? CompletedTimeTo { get; init; }

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
