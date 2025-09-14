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
    /// <returns>The instance id for the new orchestration.</returns>
    public virtual string ScheduleNewOrchestration(TaskName name, StartOrchestrationOptions options)
        => this.ScheduleNewOrchestration(name, null, options);

    /// <summary>
    /// Starts an orchestration.
    /// </summary>
    /// <param name="name">The name of the orchestration to start.</param>
    /// <param name="input">The input for the orchestration.</param>
    /// <param name="options">The options for starting the orchestration.</param>
    /// <returns>The instance id for the new orchestration.</returns>
    public abstract string ScheduleNewOrchestration(
        TaskName name, object? input = null, StartOrchestrationOptions? options = null);

    /// <summary>
    /// Starts an orchestration.
    /// </summary>
    /// <param name="name">The name of the orchestration to start.</param>
    /// <param name="input">The input for the orchestration.</param>
    /// <param name="options">The options for starting the orchestration.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public virtual ValueTask<string> ScheduleNewOrchestrationAsync(
        TaskName name,
        object? input = null,
        StartOrchestrationOptions? options = null)
    {
        return new ValueTask<string>(this.ScheduleNewOrchestration(name, input, options));
    }

    /// <summary>
    /// Signals an entity operation asynchronously.
    /// </summary>
    /// <param name="id">The entity to signal.</param>
    /// <param name="operationName">The operation name.</param>
    /// <param name="input">The operation input.</param>
    /// <param name="options">The options to signal with.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public virtual ValueTask SignalEntityAsync(
        EntityInstanceId id,
        string operationName,
        object? input = null,
        SignalEntityOptions? options = null)
    {
        this.SignalEntity(id, operationName, input, options);
        return default;
    }
}
