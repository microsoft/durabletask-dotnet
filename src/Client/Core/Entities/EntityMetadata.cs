// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask.Client.Entities;

/// <summary>
/// Represents entity metadata.
/// </summary>
/// <typeparam name="TState">The type of state held by the metadata.</typeparam>
/// <param name="id">The ID of the entity.</param>
[JsonConverter(typeof(EntityMetadataConverter))]
public class EntityMetadata<TState>(EntityInstanceId id)
{
    readonly TState? state;

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
    public EntityInstanceId Id { get; } = Check.NotDefault(id);

    /// <summary>
    /// Gets the time the entity was last modified.
    /// </summary>
    public DateTimeOffset LastModifiedTime { get; init; }

    /// <summary>
    /// Gets the size of the backlog queue, if there is a backlog, and if that metric is supported by the backend.
    /// </summary>
    public int BacklogQueueSize { get; init; }

    /// <summary>
    /// Gets the instance id of the orchestration that has locked this entity, or null if the entity is not locked.
    /// </summary>
    public string? LockedBy { get; init; }

    /// <summary>
    /// Gets a value indicating whether this metadata response includes the entity state.
    /// </summary>
    /// <remarks>
    /// Queries can exclude the state of the entity from the metadata that is retrieved.
    /// </remarks>
    [MemberNotNullWhen(true, nameof(State))]
    [MemberNotNullWhen(true, nameof(state))]
    public bool IncludesState { get; } = false;

    /// <summary>
    /// Gets the state for this entity.
    /// </summary>
    /// <remarks>
    /// This method can only be used when <see cref="IncludesState"/> = <c>true</c>, meaning  that the entity state was
    /// included in the response returned by the query.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown if this metadata object was fetched without including the entity state. In which case,
    /// <see cref="IncludesState" /> will be <c>false</c>.
    /// </exception>
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
/// <param name="id">The ID of the entity.</param>
/// <param name="state">The state of this entity.</param>
[JsonConverter(typeof(EntityMetadataConverter))]
public sealed class EntityMetadata(EntityInstanceId id, SerializedData? state = null)
    : EntityMetadata<SerializedData>(id, state)
{
}
