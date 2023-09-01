// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.Entities.OperationFormat;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.Shims;

/// <summary>
/// Shim that provides the entity context and implements batched execution.
/// </summary>
class TaskEntityShimOperation : TaskEntityOperation
{
    readonly TaskEntityShim taskEntityShim;
    readonly OperationRequest operationRequest;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskEntityShimOperation"/> class.
    /// </summary>
    /// <param name="taskEntityShim">The shim to which this operation belongs.</param>
    /// <param name="operationRequest">The operation request.</param>
    public TaskEntityShimOperation(TaskEntityShim taskEntityShim, OperationRequest operationRequest)
    {
        this.taskEntityShim = taskEntityShim;
        this.operationRequest = operationRequest;
    }

    /// <inheritdoc/>
    public override string Name => this.operationRequest.Operation ?? throw new ArgumentNullException("operation name must not be null");

    /// <inheritdoc/>
    public override TaskEntityContext Context => this.taskEntityShim;

    /// <inheritdoc/>
    public override bool HasInput => this.operationRequest.Input != null;

    /// <inheritdoc/>
    public override object? GetInput(Type inputType)
        => this.taskEntityShim.DataConverter.Deserialize(this.operationRequest.Input, inputType);

}
