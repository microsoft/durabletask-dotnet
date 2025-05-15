// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;

namespace Microsoft.DurableTask.Sidecar.Dispatcher;

public class GrpcOrchestratorExecutionResult : OrchestratorExecutionResult
{
    public string? OrchestrationActivityId { get; set; }
    public string? OrchestrationActivitySpanId { get; set; }
    public DateTimeOffset? OrchestrationActivityStartTime { get; set; }
}
