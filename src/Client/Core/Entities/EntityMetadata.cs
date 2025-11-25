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
/// <remarks>
/// Initializes a new instance of the <see cref="EntityMetadata{TState}"/> class.
/// </remarks>
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
    /// Initializes a new instance of the <see cref="EntityMetadata{TState}"/> class.
    /// </summary>
    /// <param name="id">The ID of the entity.</param>
    /// <param name="state">The state of the entity.</param>
    /// <param name="stateRequested">Whether state was requested in the query.</param>
    public EntityMetadata(EntityInstanceId id, TState? state, bool stateRequested)
        : this(id)
    {
        this.IncludesState = state is not null;
        this.StateRequested = stateRequested;
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
    /// Gets a value indicating whether state was requested in the query that produced this metadata.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property is <c>true</c> when the query was made with <c>IncludeState = true</c>.
    /// </para>
    /// <para>
    /// When <see cref="StateRequested"/> is <c>true</c> but <see cref="IncludesState"/> is <c>false</c>,
    /// it means the entity is "transient" (has no user-defined state). This can happen when an entity
    /// is in the process of being created or deleted, or when the entity has been logically deleted but
    /// the backend is still tracking metadata for synchronization purposes.
    /// </para>
    /// </remarks>
    public bool StateRequested { get; }

    /// <summary>
    /// Gets a value indicating whether this metadata response includes the entity state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property is <c>false</c> when:
    /// <list type="bullet">
    /// <item><description>The query was made with <c>IncludeState = false</c> (check <see cref="StateRequested"/>).</description></item>
    /// <item><description>The entity is "transient" (has no user-defined state). This can happen when an entity
    /// is in the process of being created or deleted, or when the entity has been logically deleted but
    /// the backend is still tracking metadata for synchronization purposes.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// To distinguish between these cases, check <see cref="StateRequested"/>: if it is <c>true</c> but
    /// <see cref="IncludesState"/> is <c>false</c>, the entity is transient.
    /// </para>
    /// <para>
    /// To query for transient entities, use <see cref="EntityQuery.IncludeTransient"/> = <c>true</c>.
    /// </para>
    /// </remarks>
    [MemberNotNullWhen(true, nameof(State))]
    [MemberNotNullWhen(true, nameof(state))]
    public bool IncludesState { get; } = false;

    /// <summary>
    /// Gets a value indicating whether the entity has state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a convenience property that returns <c>true</c> when <see cref="StateRequested"/> is <c>true</c>
    /// and <see cref="IncludesState"/> is <c>false</c>, indicating the entity is "transient" (has no user-defined state).
    /// </para>
    /// <para>
    /// Note: This property is only meaningful when <see cref="StateRequested"/> is <c>true</c>.
    /// If <see cref="StateRequested"/> is <c>false</c>, you cannot determine whether the entity has state.
    /// </para>
    /// </remarks>
    public bool IsTransient => this.StateRequested && !this.IncludesState;

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
[JsonConverter(typeof(EntityMetadataConverter))]
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

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityMetadata"/> class.
    /// </summary>
    /// <param name="id">The ID of the entity.</param>
    /// <param name="state">The state of this entity.</param>
    /// <param name="stateRequested">Whether state was requested in the query.</param>
    public EntityMetadata(EntityInstanceId id, SerializedData? state, bool stateRequested)
        : base(id, state, stateRequested)
    {
    }
}
