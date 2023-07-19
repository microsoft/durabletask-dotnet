// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Entities;

/// <summary>
/// Feature for interacting with durable entities from an orchestration.
/// </summary>
public abstract class TaskOrchestrationEntityFeature
{
    /// <summary>
    /// Calls an operation on an entity and waits for it to complete. Does not wait for completion if
    /// <see cref="CallEntityOptions.Signal"/> is set on <paramref name="options"/>.
    /// </summary>
    /// <typeparam name="TResult">The result of the entity operation.</typeparam>
    /// <param name="id">The target entity.</param>
    /// <param name="operationName">The name of the operation.</param>
    /// <param name="input">The operation input.</param>
    /// <param name="options">The call options.</param>
    /// <returns>
    /// The result of the entity operation, or default(<typeparamref name="TResult"/>) in the signal-only case.
    /// </returns>
    public abstract Task<TResult> CallEntityAsync<TResult>(
        EntityInstanceId id, string operationName, object? input = null, CallEntityOptions? options = null);

    /// <summary>
    /// Calls an operation on an entity and waits for it to complete. Does not wait for completion if
    /// <see cref="CallEntityOptions.Signal"/> is set on <paramref name="options"/>.
    /// </summary>
    /// <typeparam name="TResult">The result of the entity operation.</typeparam>
    /// <param name="id">The target entity.</param>
    /// <param name="operationName">The name of the operation.</param>
    /// <param name="options">The call options.</param>
    /// <returns>
    /// The result of the entity operation, or default(<typeparamref name="TResult"/>) in the signal-only case.
    /// </returns>
    public virtual Task<TResult> CallEntityAsync<TResult>(
        EntityInstanceId id, string operationName, CallEntityOptions? options)
        => this.CallEntityAsync<TResult>(id, operationName, null, options);

    /// <summary>
    /// Calls an operation on an entity and waits for it to complete. Does not wait for completion if
    /// <see cref="CallEntityOptions.Signal"/> is set on <paramref name="options"/>.
    /// </summary>
    /// <param name="id">The target entity.</param>
    /// <param name="operationName">The name of the operation.</param>
    /// <param name="input">The operation input.</param>
    /// <param name="options">The call options.</param>
    /// <returns>
    /// A task that completes when the operation has been completed. Or in the signal-only case, a task that is either
    /// already complete or completes when the operation has been enqueued.
    /// </returns>
    public abstract Task CallEntityAsync(
        EntityInstanceId id, string operationName, object? input = null, CallEntityOptions? options = null);

    /// <summary>
    /// Calls an operation on an entity and waits for it to complete. Does not wait for completion if
    /// <see cref="CallEntityOptions.Signal"/> is set on <paramref name="options"/>.
    /// </summary>
    /// <param name="id">The target entity.</param>
    /// <param name="operationName">The name of the operation.</param>
    /// <param name="options">The call options.</param>
    /// <returns>
    /// A task that completes when the operation has been completed. Or in the signal-only case, a task that is either
    /// already complete or completes when the operation has been enqueued.
    /// </returns>
    public virtual Task CallEntityAsync(EntityInstanceId id, string operationName, CallEntityOptions? options)
        => this.CallEntityAsync(id, operationName, null, options);

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
    /// Gets a value indicating whether any entity locks are owned by this instance.
    /// </summary>
    /// <param name="entityIds">The list of locked entities.</param>
    /// <returns>True if any locks are owned, false otherwise.</returns>
    public abstract bool HasEntityLocks(out IReadOnlyList<EntityInstanceId> entityIds);

    /// <summary>
    /// Gets a value indicating whether any entity locks are owned by this instance.
    /// </summary>
    /// <returns>True if any locks are owned, false otherwise.</returns>
    public virtual bool HasEntityLocks() => this.HasEntityLocks(out _);
}
