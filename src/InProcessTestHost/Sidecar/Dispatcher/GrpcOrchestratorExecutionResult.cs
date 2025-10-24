// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;

namespace Microsoft.DurableTask.Sidecar.Dispatcher;

/// <summary>
/// Grpc orchestration execution result.
/// </summary>
public class GrpcOrchestratorExecutionResult : OrchestratorExecutionResult
{
    /// <summary>
    /// Gets or sets the orcehstration activity spanId.
    /// </summary>
    public string? OrchestrationActivitySpanId { get; set; }

    /// <summary>
    /// Gets or sets the orchestration activity start time.
    /// </summary>
    public DateTimeOffset? OrchestrationActivityStartTime { get; set; }
}
