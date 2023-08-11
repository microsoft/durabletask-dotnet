// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Entities;

/// <summary>
/// The context for a <see cref="ITaskEntity"/>.
/// </summary>
public abstract class TaskEntityContext
{
    /// <summary>
    /// Gets the instance ID of this entity.
    /// </summary>
    public abstract EntityInstanceId Id { get; }

    /// <summary>
    /// Signals an entity operation.
    /// </summary>
    /// <param name="id">The entity to signal.</param>
    /// <param name="operationName">The operation name.</param>
    /// <param name="options">The options to signal with.</param>
    public virtual void SignalEntity(EntityInstanceId id, string operationName, SignalEntityOptions options)
        => this.SignalEntity(id, operationName, null, options);

    /// <summary>
    /// Signals an entity operation.
    /// </summary>
    /// <param name="id">The entity to signal.</param>
    /// <param name="operationName">The operation name.</param>
    /// <param name="input">The operation input.</param>
    /// <param name="options">The options to signal with.</param>
    public abstract void SignalEntity(
        EntityInstanceId id,
        string operationName,
        object? input = null,
        SignalEntityOptions? options = null);

    /// <summary>
    /// Starts an orchestration.
    /// </summary>
    /// <param name="name">The name of the orchestration to start.</param>
    /// <param name="options">The options for starting the orchestration.</param>
    public virtual void StartOrchestration(TaskName name, StartOrchestrationOptions options)
        => this.StartOrchestration(name, null, options);

    /// <summary>
    /// Starts an orchestration.
    /// </summary>
    /// <param name="name">The name of the orchestration to start.</param>
    /// <param name="input">The input for the orchestration.</param>
    /// <param name="options">The options for starting the orchestration.</param>
    public abstract void StartOrchestration(
        TaskName name, object? input = null, StartOrchestrationOptions? options = null);

    /// <summary>
    /// Gets the current state for the entity this context is for.
    /// </summary>
    /// <param name="type">The type to retrieve the state as.</param>
    /// <returns>The entity state.</returns>
    public abstract object? GetState(Type type);

    /// <summary>
    /// Sets the entity state. Setting of <c>null</c> will clear entity state.
    /// </summary>
    /// <param name="state">The state to set.</param>
    public abstract void SetState(object? state);
}
