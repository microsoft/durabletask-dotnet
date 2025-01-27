// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.DurableTask.Entities;

/// <summary>
/// Represents the ID of an entity.
/// </summary>
[JsonConverter(typeof(EntityInstanceId.JsonConverter))]
public readonly record struct EntityInstanceId
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EntityInstanceId"/> struct.
    /// </summary>
    /// <param name="name">The entity name.</param>
    /// <param name="key">The entity key.</param>
    public EntityInstanceId(string name, string key)
    {
        Check.NotNullOrEmpty(name);
        if (name.Contains('@'))
        {
            throw new ArgumentException("entity names may not contain `@` characters.", nameof(name));
        }

        Check.NotNull(key);
        this.Name = name.ToLowerInvariant();
        this.Key = key;
    }

    /// <summary>
    /// Gets the entity name. Entity names are normalized to lower case.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the entity key.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Constructs a <see cref="EntityInstanceId"/> from a string containing the instance ID.
    /// </summary>
    /// <param name="instanceId">The string representation of the entity ID.</param>
    /// <returns>the constructed entity instance ID.</returns>
    public static EntityInstanceId FromString(string instanceId)
    {
        Check.NotNullOrEmpty(instanceId);
        var pos = instanceId.IndexOf('@', 1);
        if (pos <= 0 || instanceId[0] != '@')
        {
            throw new ArgumentException($"Instance ID '{instanceId}' is not a valid entity ID.", nameof(instanceId));
        }

        var entityName = instanceId.Substring(1, pos - 1);
        var entityKey = instanceId.Substring(pos + 1);
        return new EntityInstanceId(entityName, entityKey);
    }

    /// <inheritdoc/>
    public override string ToString() => $"@{this.Name}@{this.Key}";

    /// <summary>
    /// We override the default json conversion so we can use a more compact string representation for entity instance ids.
    /// </summary>
    class JsonConverter : JsonConverter<EntityInstanceId>
    {
        public override EntityInstanceId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return EntityInstanceId.FromString(reader.GetString()!);
        }

        public override void Write(Utf8JsonWriter writer, EntityInstanceId value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString()!);
        }
    }
}
