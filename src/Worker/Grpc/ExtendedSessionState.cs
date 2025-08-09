// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;

namespace Microsoft.DurableTask.Worker.Grpc;

/// <summary>
/// Represents the state of an extended session for an orchestration.
/// </summary>
class ExtendedSessionState
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExtendedSessionState"/> class.
    /// </summary>
    /// <param name="state">The orchestration's runtime state.</param>
    /// <param name="taskOrchestration">The TaskOrchestration implementation of the orchestration.</param>
    /// <param name="orchestrationExecutor">The TaskOrchestrationExecutor for the orchestration.</param>
    internal ExtendedSessionState(OrchestrationRuntimeState state, TaskOrchestration taskOrchestration, TaskOrchestrationExecutor orchestrationExecutor)
    {
        this.RuntimeState = state;
        this.TaskOrchestration = taskOrchestration;
        this.OrchestrationExecutor = orchestrationExecutor;
    }

    /// <summary>
    /// Gets or sets the saved runtime state of the orchestration.
    /// </summary>
    internal OrchestrationRuntimeState RuntimeState { get; set; }

    /// <summary>
    /// Gets or sets the saved TaskOrchestration implementation of the orchestration.
    /// </summary>
    internal TaskOrchestration TaskOrchestration { get; set; }

    /// <summary>
    /// Gets or sets the saved TaskOrchestrationExecutor.
    /// </summary>
    internal TaskOrchestrationExecutor OrchestrationExecutor { get; set; }
}
