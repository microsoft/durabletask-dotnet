// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask.ExportHistory;

public record ExportFilter(
    DateTimeOffset CompletedTimeFrom,
    DateTimeOffset? CompletedTimeTo = null,
    IEnumerable<OrchestrationRuntimeStatus>? RuntimeStatus = null);