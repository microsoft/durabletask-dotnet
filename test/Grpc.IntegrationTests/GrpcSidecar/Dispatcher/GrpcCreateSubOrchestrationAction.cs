// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core.Command;
using DurableTask.Core.Tracing;

namespace Microsoft.DurableTask.Sidecar.Dispatcher;

public class GrpcCreateSubOrchestrationAction : CreateSubOrchestrationAction
{
    public DistributedTraceContext? ParentTraceContext { get; set; }
}
