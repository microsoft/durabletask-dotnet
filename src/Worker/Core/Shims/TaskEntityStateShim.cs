// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.Entities.OperationFormat;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.Shims;

/// <summary>
/// TaskEntityContext implementation.
/// </summary>
class TaskEntityContextShim : TaskEntityContext
{
    readonly TaskEntityShim taskEntityShim;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskEntityStateShim"/> class.
    /// </summary>
    /// <param name="taskEntityShim">The taskEntityShim that owns this context.</param>
    public TaskEntityContextShim(TaskEntityShim taskEntityShim)
    {
        this.taskEntityShim = taskEntityShim;
    }

    /// <inheritdoc/>
    public override EntityInstanceId Id => this.taskEntityShim.Id;

    /// <inheritdoc/>
    public override void SignalEntity(EntityInstanceId id, string operationName, object? input = null, SignalEntityOptions? options = null)
    {
        this.taskEntityShim.SignalEntity(id, operationName, input, options);
    }

    /// <inheritdoc/>
    public override void StartOrchestration(TaskName name, object? input = null, StartOrchestrationOptions? options = null)
    {
        this.taskEntityShim.StartOrchestration(name, input, options);
    }
}
