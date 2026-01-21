// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Filter criteria for selecting orchestration instances to export.
/// </summary>
/// <param name="CompletedTimeFrom">The start time for the export based on completion time (inclusive).</param>
/// <param name="CompletedTimeTo">The end time for the export based on completion time (inclusive). Optional.</param>
/// <param name="RuntimeStatus">The orchestration runtime statuses to filter by. Optional.</param>
public record ExportFilter(
    DateTimeOffset CompletedTimeFrom,
    DateTimeOffset? CompletedTimeTo = null,
    IEnumerable<OrchestrationRuntimeStatus>? RuntimeStatus = null);
