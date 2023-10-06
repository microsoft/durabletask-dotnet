// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Entities;

/// <summary>
/// Describes a single operation for a <see cref="ITaskEntity"/>.
/// </summary>
public abstract class TaskEntityOperation
{
    /// <summary>
    /// Gets the name of the operation.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Gets the context for this entity operation.
    /// </summary>
    public abstract TaskEntityContext Context { get; }

    /// <summary>
    /// Gets the state of the entity.
    /// </summary>
    public abstract TaskEntityState State { get; }

    /// <summary>
    /// Gets a value indicating whether this operation has input or not.
    /// </summary>
    public abstract bool HasInput { get; }

    /// <summary>
    /// Gets the input for this operation.
    /// </summary>
    /// <typeparam name="T">The type to deserialize the input as.</typeparam>
    /// <returns>The deserialized input type.</returns>
    public virtual T? GetInput<T>() => (T?)this.GetInput(typeof(T));

    /// <summary>
    /// Gets the input for this operation.
    /// </summary>
    /// <param name="inputType">The type to deserialize the input as.</param>
    /// <returns>The deserialized input type.</returns>
    public abstract object? GetInput(Type inputType);

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"{this.Context.Id.Name}/{this.Name}";
    }
}
