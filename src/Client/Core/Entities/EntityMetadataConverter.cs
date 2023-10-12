// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.DurableTask.Client.Entities;

/// <summary>
/// Json converter factory for <see cref="EntityMetadata{TState}"/> .
/// </summary>
class EntityMetadataConverter : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
    {
        if (typeToConvert.IsGenericType)
        {
            Type genericType = typeToConvert.GetGenericTypeDefinition();
            if (genericType == typeof(EntityMetadata<>))
            {
                return true;
            }
        }

        if (typeToConvert == typeof(EntityMetadata))
        {
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (typeToConvert == typeof(EntityMetadata))
        {
            return new Converter();
        }

        Type stateType = typeToConvert.GetGenericArguments()[0];
        return (JsonConverter)Activator.CreateInstance(
            typeof(Converter<>).MakeGenericType(stateType),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            args: null,
            culture: null)!;
    }

    class Converter<TState> : JsonConverter<EntityMetadata<TState>>
    {
        public override EntityMetadata<TState>? Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotSupportedException("EntityMetadata cannot be deserialized");
        }

        public override void Write(Utf8JsonWriter writer, EntityMetadata<TState> value, JsonSerializerOptions options)
        {
            EntityMetadataConverter.Write(writer, value, options);
        }
    }
    
    class Converter : JsonConverter<EntityMetadata>
    {
        public override EntityMetadata? Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotSupportedException("EntityMetadata cannot be deserialized");
        }

        public override void Write(Utf8JsonWriter writer, EntityMetadata value, JsonSerializerOptions options)
        {
            EntityMetadataConverter.Write(writer, value, options);
        }
    }

    static void Write<TState>(Utf8JsonWriter writer, EntityMetadata<TState> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        static string ConvertName(string name, JsonSerializerOptions options)
        {
            return options.PropertyNamingPolicy?.ConvertName(name) ?? name;
        }

        writer.WriteString(ConvertName(nameof(value.Id), options), value.Id.ToString());
        writer.WriteString(ConvertName(nameof(value.LastModifiedTime), options), value.LastModifiedTime);

        if (value.BacklogQueueSize > 0)
        {
            writer.WriteNumber(ConvertName(nameof(value.BacklogQueueSize), options), value.BacklogQueueSize);
        }

        if (value.LockedBy is string s)
        {
            writer.WriteString(ConvertName(nameof(value.LockedBy), options), value.LockedBy);
        }

        if (value.IncludesState)
        {
            if (value is EntityMetadata<SerializedData> serializedData)
            {
                writer.WriteString(ConvertName(nameof(value.State), options), serializedData.State.Value);
            }
            else
            {
                writer.WritePropertyName(ConvertName(nameof(value.State), options));
                JsonSerializer.Serialize(writer, value.State, options);
            }
        }

        writer.WriteEndObject();
    }
}
