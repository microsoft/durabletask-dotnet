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
    /// <param name="input">The input for the orchestration.</param>
    /// <param name="options">The options for starting the orchestration.</param>
    public abstract void StartOrchestration(
        TaskName name, object? input = null, StartOrchestrationOptions? options = null);

    /// <summary>
    /// Deletes this current entities state after the current operation completes.
    /// </summary>
    /// <remarks>
    /// The state deletion only takes effect after the current operation completes. Any state changes made during the
    /// current operation will be ignored in favor of the deletion.
    /// </remarks>
    public abstract void DeleteState();
}
