// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.Entities.OperationFormat;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.Shims;

/// <inheritdoc/>
class TaskEntityShim<TState> : TaskEntity<TState>
{
    readonly EntityInstanceId instanceId;
    readonly ITaskEntity implementation;
    readonly EntityInvocationContext invocationContext;

    List<OperationAction> operationActions;
    string? lastSerializedState;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskEntityShim"/> class.
    /// </summary>
    /// <param name="invocationContext">The invocation context for this orchestration.</param>
    /// <param name="implementation">The orchestration's implementation.</param>
    public TaskEntityShim(
        EntityInvocationContext invocationContext,
        ITaskEntity implementation)
    {
        this.invocationContext = Check.NotNull(invocationContext);
        this.implementation = Check.NotNull(implementation);
        this.operationActions = new List<OperationAction>();
        this.lastSerializedState = null;
    }

}

