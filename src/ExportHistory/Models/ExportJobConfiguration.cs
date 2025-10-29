// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

public record ExportJobConfiguration(
    ExportMode Mode,
    ExportFilter Filter,
    ExportDestination Destination,
    ExportFormat Format,
    int MaxParallelExports = 32,
    int MaxInstancesPerBatch = 100);