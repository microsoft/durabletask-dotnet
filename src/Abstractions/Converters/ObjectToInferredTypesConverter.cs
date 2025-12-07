// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.DurableTask.Converters;

/// <summary>
/// A custom JSON converter that deserializes JSON tokens into inferred .NET types instead of JsonElement.
/// </summary>
/// <remarks>
/// When deserializing to <c>object</c> type, System.Text.Json defaults to returning JsonElement instances.
/// This converter infers the appropriate .NET type based on the JSON token type:
/// - JSON strings become <see cref="string"/>
/// - JSON numbers become <see cref="int"/>, <see cref="long"/>, or <see cref="double"/>
/// - JSON booleans become <see cref="bool"/>
/// - JSON objects become <see cref="Dictionary{TKey, TValue}"/> where TValue is <see cref="object"/>
/// - JSON arrays become <see cref="object"/>[]
/// - JSON null becomes null
/// This is particularly useful when working with <see cref="Dictionary{TKey, TValue}"/> where TValue is
/// <see cref="object"/>, ensuring that complex types are preserved as dictionaries rather than JsonElement.
/// </remarks>
internal sealed class ObjectToInferredTypesConverter : JsonConverter<object>
{
    /// <inheritdoc/>
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True:
            case JsonTokenType.False:
                return reader.GetBoolean();
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out int intValue))
                {
                    return intValue;
                }

                if (reader.TryGetInt64(out long longValue))
                {
                    return longValue;
                }

                return reader.GetDouble();
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.StartObject:
                Dictionary<string, object?> dictionary = new();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        throw new JsonException("Expected property name.");
                    }

                    string propertyName = reader.GetString() ?? throw new JsonException("Property name cannot be null.");
                    reader.Read();
                    dictionary[propertyName] = this.Read(ref reader, typeof(object), options);
                }

                return dictionary;
            case JsonTokenType.StartArray:
                List<object?> list = new();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    list.Add(this.Read(ref reader, typeof(object), options));
                }

                return list.ToArray();
            case JsonTokenType.Null:
                return null;
            default:
                throw new JsonException($"Unsupported JSON token type: {reader.TokenType}");
        }
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value?.GetType() ?? typeof(object), options);
    }
}
