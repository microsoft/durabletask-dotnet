// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Configuration for an export job.
/// </summary>
/// <param name="Mode">The export mode (Batch or Continuous).</param>
/// <param name="Filter">The filter criteria for selecting orchestration instances to export.</param>
/// <param name="Destination">The export destination where exported data will be stored.</param>
/// <param name="Format">The export format settings.</param>
/// <param name="MaxParallelExports">The maximum number of parallel export operations. Defaults to 32.</param>
/// <param name="MaxInstancesPerBatch">The maximum number of instances to fetch per batch. Defaults to 100.</param>
public record ExportJobConfiguration(
    ExportMode Mode,
    ExportFilter Filter,
    ExportDestination Destination,
    ExportFormat Format,
    int MaxParallelExports = 32,
    int MaxInstancesPerBatch = 100);
