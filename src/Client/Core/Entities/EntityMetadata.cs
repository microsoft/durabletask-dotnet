// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask.Client.Entities;

/// <summary>
/// Represents entity metadata.
/// </summary>
/// <typeparam name="TState">The type of state held by the metadata.</typeparam>
public class EntityMetadata<TState>
{
    readonly TState? state;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityMetadata{TState}"/> class.
    /// </summary>
    /// <param name="id">The ID of the entity.</param>
    public EntityMetadata(EntityInstanceId id)
    {
        this.Id = Check.NotDefault(id);
        this.IncludesState = false;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityMetadata{TState}"/> class.
    /// </summary>
    /// <param name="id">The ID of the entity.</param>
    /// <param name="state">The state of the entity.</param>
    public EntityMetadata(EntityInstanceId id, TState? state)
        : this(id)
    {
        this.IncludesState = state is not null;
        this.state = state;
    }

    /// <summary>
    /// Gets the ID for this entity.
    /// </summary>
    public EntityInstanceId Id { get; }

    /// <summary>
    /// Gets the time the entity was last modified.
    /// </summary>
    public DateTimeOffset LastModifiedTime { get; init; }

    /// <summary>
    /// Gets a value indicating if entity metadata 
    /// </summary>
    [MemberNotNullWhen(true, "State")]
    [MemberNotNullWhen(true, "state")]
    public bool IncludesState { get; }

    /// <summary>
    /// Gets the state for this entity.
    /// </summary>
    public TState State
    {
        get
        {
            if (this.IncludesState)
            {
                return this.state;
            }

            throw new InvalidOperationException($"Cannot retrieve state when {nameof(this.IncludesState)}=false");
        }
    }
}

/// <summary>
/// Represents the metadata for a durable entity instance.
/// </summary>
public sealed class EntityMetadata : EntityMetadata<SerializedData>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EntityMetadata"/> class.
    /// </summary>
    /// <param name="id">The ID of the entity.</param>
    /// <param name="state">The state of this entity.</param>
    public EntityMetadata(EntityInstanceId id, SerializedData? state = null)
        : base(id, state)
    {
    }
}
