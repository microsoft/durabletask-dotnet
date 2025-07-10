// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;

namespace Microsoft.DurableTask.Worker.Grpc;

/// <summary>
/// 
/// </summary>
class ExtendedSessionState
{
    internal OrchestrationRuntimeState RuntimeState { get; set; }

    internal TaskOrchestration TaskOrchestration { get; set; }

    internal TaskOrchestrationExecutor OrchestrationExecutor { get; set; }

    public ExtendedSessionState(OrchestrationRuntimeState state, TaskOrchestration taskOrchestration, TaskOrchestrationExecutor orchestrationExecutor)
    {
        RuntimeState = state;
        TaskOrchestration = taskOrchestration;
        OrchestrationExecutor = orchestrationExecutor;
    }
}
