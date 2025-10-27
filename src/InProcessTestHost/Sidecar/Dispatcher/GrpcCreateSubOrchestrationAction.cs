// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core.Command;
using DurableTask.Core.Tracing;

namespace Microsoft.DurableTask.Testing.Sidecar.Dispatcher;

/// <summary>
/// Action for creating sub-orchestration.
/// </summary>
public class GrpcCreateSubOrchestrationAction : CreateSubOrchestrationAction
{
    /// <summary>
    /// Gets or sets distributed parent trace context.
    /// </summary>
    public DistributedTraceContext? ParentTraceContext { get; set; }
}
