// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask.Client.Entities;

/// <summary>
/// Represents the metadata for a durable entity instance.
/// </summary>
public class EntityMetadata
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EntityMetadata"/> class.
    /// </summary>
    /// <param name="id">The ID of the entity.</param>
    public EntityMetadata(EntityInstanceId id)
    {
        this.Id = Check.NotDefault(id);
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
    /// Gets the data converter for this metadata.
    /// </summary>
    public DataConverter? DataConverter { get; init; }

    /// <summary>
    /// Gets the serialized state for this entity.
    /// </summary>
    public string? SerializedState { get; init; }

    /// <summary>
    /// Deserializes the entity's state into an object of the specified type.
    /// </summary>
    /// <remarks>
    /// This method can only be used when state are explicitly requested from the
    /// <see cref="DurableEntityClient.GetEntityAsync(EntityInstanceId, CancellationToken)"/> or
    /// <see cref="DurableEntityClient.GetAllEntitiesAsync(EntityQuery)"/> method that produced
    /// this <see cref="EntityMetadata"/> object.
    /// </remarks>
    /// <typeparam name="T">The type to deserialize the entity state into.</typeparam>
    /// <returns>Returns the deserialized state value.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if this metadata object was fetched without the option to read state.
    /// </exception>
    public T? ReadStateAs<T>()
    {
        if (this.DataConverter is null)
        {
            throw new InvalidOperationException(
                $"The {nameof(this.ReadStateAs)} method can only be used on {nameof(EntityMetadata)} objects " +
                "that are fetched with the option to include state data.");
        }

        return this.DataConverter.Deserialize<T>(this.SerializedState);
    }
}
