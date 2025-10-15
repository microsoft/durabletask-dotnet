// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core.Command;
using DurableTask.Core.Tracing;

namespace Microsoft.DurableTask.Sidecar.Dispatcher;

/// <summary>
/// gRPC-specific implementation of ScheduleTaskOrchestratorAction that includes distributed tracing context.
/// </summary>
public class GrpcScheduleTaskOrchestratorAction : ScheduleTaskOrchestratorAction
{   
    /// <summary>
    /// Gets or sets the parent trace context for distributed tracing.
    /// </summary>
    public DistributedTraceContext? ParentTraceContext { get; set; }
}
