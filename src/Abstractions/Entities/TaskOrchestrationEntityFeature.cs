// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace Microsoft.DurableTask.Entities;

/// <summary>
/// Feature for interacting with durable entities from an orchestration.
/// </summary>
public abstract class TaskOrchestrationEntityFeature
{
    /// <summary>
    /// Calls an operation on an entity and waits for it to complete.
    /// </summary>
    /// <typeparam name="TResult">The result type of the entity operation.</typeparam>
    /// <param name="id">The target entity.</param>
    /// <param name="operationName">The name of the operation.</param>
    /// <param name="input">The operation input.</param>
    /// <param name="options">The call options.</param>
    /// <returns>The result of the entity operation.</returns>
    public abstract Task<TResult> CallEntityAsync<TResult>(
        EntityInstanceId id, string operationName, object? input = null, CallEntityOptions? options = null);

    /// <summary>
    /// Calls an operation on an entity and waits for it to complete.
    /// </summary>
    /// <typeparam name="TResult">The result type of the entity operation.</typeparam>
    /// <param name="id">The target entity.</param>
    /// <param name="operationName">The name of the operation.</param>
    /// <param name="options">The call options.</param>
    /// <returns>The result of the entity operation.</returns>
    public virtual Task<TResult> CallEntityAsync<TResult>(
        EntityInstanceId id, string operationName, CallEntityOptions? options)
        => this.CallEntityAsync<TResult>(id, operationName, null, options);

    /// <summary>
    /// Calls an operation on an entity and waits for it to complete.
    /// </summary>
    /// <param name="id">The target entity.</param>
    /// <param name="operationName">The name of the operation.</param>
    /// <param name="input">The operation input.</param>
    /// <param name="options">The call options.</param>
    /// <returns>A task that completes when the operation has been completed.</returns>
    public abstract Task CallEntityAsync(
        EntityInstanceId id, string operationName, object? input = null, CallEntityOptions? options = null);

    /// <summary>
    /// Calls an operation on an entity and waits for it to complete.
    /// </summary>
    /// <param name="id">The target entity.</param>
    /// <param name="operationName">The name of the operation.</param>
    /// <param name="options">The call options.</param>
    /// <returns>A task that completes when the operation has been completed.</returns>
    public virtual Task CallEntityAsync(EntityInstanceId id, string operationName, CallEntityOptions? options)
        => this.CallEntityAsync(id, operationName, null, options);

    /// <summary>
    /// Calls an operation on an entity, but does not wait for completion.
    /// </summary>
    /// <param name="id">The target entity.</param>
    /// <param name="operationName">The name of the operation.</param>
    /// <param name="input">The operation input.</param>
    /// <param name="options">The signal options.</param>
    /// <returns>
    /// A task that represents scheduling of the signal operation. Dependening on implementation, this may complete
    /// either when the operation has been signalled, or when the signal action has been enqueued by the context.
    /// </returns>
    public abstract Task SignalEntityAsync(
        EntityInstanceId id, string operationName, object? input = null, SignalEntityOptions? options = null);

    /// <summary>
    /// Calls an operation on an entity, but does not wait for completion.
    /// </summary>
    /// <param name="id">The target entity.</param>
    /// <param name="operationName">The name of the operation.</param>
    /// <param name="options">The signal options.</param>
    /// <returns>
    /// A task that represents scheduling of the signal operation. Dependening on implementation, this may complete
    /// either when the operation has been signalled, or when the signal action has been enqueued by the context.
    /// </returns>
    public virtual Task SignalEntityAsync(
        EntityInstanceId id, string operationName, SignalEntityOptions? options)
        => this.SignalEntityAsync(id, operationName, null, options);

    /// <summary>
    /// Acquires one or more entity locks.
    /// </summary>
    /// <param name="entityIds">The entity IDs to lock.</param>
    /// <returns>An async-disposable which can be disposed to release the lock.</returns>
    public abstract Task<IAsyncDisposable> LockEntitiesAsync(IEnumerable<EntityInstanceId> entityIds);

    /// <summary>
    /// Acquires one or more entity locks.
    /// </summary>
    /// <param name="entityIds">The entity IDs to lock.</param>
    /// <returns>An async-disposable which can be disposed to release the lock.</returns>
    public virtual Task<IAsyncDisposable> LockEntitiesAsync(params EntityInstanceId[] entityIds)
        => this.LockEntitiesAsync((IEnumerable<EntityInstanceId>)entityIds); // let the implementation decide how to handle nulls.

    /// <summary>
    /// Gets a value indicating whether this orchestration is in a critical section, and if true, any entity locks are
    /// owned by this instance.
    /// </summary>
    /// <param name="entityIds">The list of locked entities.</param>
    /// <returns>True if any locks are owned, false otherwise.</returns>
    public abstract bool InCriticalSection([NotNullWhen(true)] out IReadOnlyList<EntityInstanceId>? entityIds);

    /// <summary>
    /// Gets a value indicating whether this orchestration is in a critical section.
    /// </summary>
    /// <returns>True if any locks are owned, false otherwise.</returns>
    public virtual bool InCriticalSection() => this.InCriticalSection(out _);
}
