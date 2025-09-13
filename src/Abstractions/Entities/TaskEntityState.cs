// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Entities;

/// <summary>
/// Represents the persisted state of an entity.
/// </summary>
public abstract class TaskEntityState
{
    /// <summary>
    /// Gets a value indicating whether this entity has state or not yet / anymore.
    /// </summary>
    public abstract bool HasState { get; }

    /// <summary>
    /// Gets the current state of the entity. This will return <c>null</c> if no state is present, regardless if
    /// <typeparamref name="T"/> is a value-type or not.
    /// </summary>
    /// <typeparam name="T">The type to retrieve.</typeparam>
    /// <param name="defaultValue">The default value to return if no state is present.</param>
    /// <returns>The entity state.</returns>
    /// <remarks>
    /// If no state is present, then <paramref see="defaultValue"/> will be returned but it will <b>not</b> be persisted
    /// to <see cref="SetState"/>. This must be manually called.
    /// </remarks>
    public virtual T? GetState<T>(T? defaultValue = default)
    {
        object? state = this.GetState(typeof(T));
        if (state is T typedState)
        {
            return typedState;
        }

        return defaultValue;
    }

    /// <summary>
    /// Asynchronously gets the current state of the entity. This will return <c>null</c> if no state is present, regardless if
    /// <paramref name="type"/> is a value-type or not.
    /// </summary>
    /// <param name="type">The type to retrieve the state as.</param>
    /// <returns>The entity state.</returns>
    public virtual Task<object?> GetStateAsync(Type type)
    {
        return Task.FromResult(this.GetState(type));
    }

    /// <summary>
    /// Gets the current state of the entity. This will return <c>null</c> if no state is present, regardless if
    /// <paramref name="type"/> is a value-type or not.
    /// </summary>
    /// <param name="type">The type to retrieve the state as.</param>
    /// <returns>The entity state.</returns>
    public abstract object? GetState(Type type);

    /// <summary>
    /// Sets the entity state. Setting of <c>null</c> will delete entity state.
    /// </summary>
    /// <param name="state">The state to set.</param>
    public abstract void SetState(object? state);
}
