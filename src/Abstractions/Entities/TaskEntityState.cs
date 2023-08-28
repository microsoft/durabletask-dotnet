﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Entities;

/// <summary>
/// Represents the persisted state of an entity.
/// </summary>
public abstract class TaskEntityState
{
    /// <summary>
    /// Gets the current state for the entity this context is for. This will return <c>null</c> if no state is present,
    /// regardless if <paramref name="type"/> is a value-type or not.
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