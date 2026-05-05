// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.DurableTask.Converters;

/// <summary>
/// A JSON converter factory that preserves type information for complex objects when deserializing to <see cref="object"/> type
/// and Dictionary&lt;string, object&gt; values. This fixes issue #430 where Dictionary&lt;string, object&gt; values and other
/// object-typed properties were being deserialized as JsonElement instead of their original types.
/// </summary>
sealed class ObjectWithTypeMetadataJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        // Handle object type
        if (typeToConvert == typeof(object))
        {
            return true;
        }

        // Handle Dictionary<string, object> and IDictionary<string, object>
        if (typeToConvert.IsGenericType)
        {
            Type genericType = typeToConvert.GetGenericTypeDefinition();
            if (genericType == typeof(Dictionary<,>) || genericType == typeof(IDictionary<,>))
            {
                Type[] args = typeToConvert.GetGenericArguments();
                if (args.Length == 2 && args[0] == typeof(string) && args[1] == typeof(object))
                {
                    return true;
                }
            }
        }

        // Also handle if the type implements IDictionary<string, object>
        if (typeof(IDictionary<string, object>).IsAssignableFrom(typeToConvert) && typeToConvert != typeof(object))
        {
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (typeToConvert == typeof(object))
        {
            return new ObjectJsonConverter();
        }

        if (typeToConvert.IsGenericType)
        {
            Type genericType = typeToConvert.GetGenericTypeDefinition();
            if (genericType == typeof(Dictionary<,>) || genericType == typeof(IDictionary<,>))
            {
                Type[] args = typeToConvert.GetGenericArguments();
                if (args.Length == 2 && args[0] == typeof(string) && args[1] == typeof(object))
                {
                    return new DictionaryStringObjectJsonConverter();
                }
            }
        }
        
        // Handle types that implement IDictionary<string, object>
        if (typeof(IDictionary<string, object>).IsAssignableFrom(typeToConvert) && typeToConvert != typeof(object))
        {
            return new DictionaryStringObjectJsonConverter();
        }

        throw new NotSupportedException($"Type {typeToConvert} is not supported by this converter factory.");
    }

    /// <summary>
    /// Converter for object type that preserves type information.
    /// </summary>
    sealed class ObjectJsonConverter : JsonConverter<object>
    {
        const string TypePropertyName = "$type";
        const string ValuePropertyName = "$value";

        /// <inheritdoc/>
        public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            // Check if this is a wrapped object with type metadata
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                // Parse the entire object to check for $type property
                // This consumes the reader
                using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
                {
                    JsonElement root = doc.RootElement;

                    if (root.TryGetProperty(TypePropertyName, out JsonElement typeElement) &&
                        typeElement.ValueKind == JsonValueKind.String)
                    {
                        // This is a wrapped object with type metadata
                        string typeName = typeElement.GetString()!;
                        Type? targetType = Type.GetType(typeName, throwOnError: false);

                    if (targetType != null && root.TryGetProperty(ValuePropertyName, out JsonElement valueElement))
                    {
                        // Deserialize the $value to the specified type
                        // Use GetRawText() to get the JSON string, then deserialize it
                        string jsonText = valueElement.GetRawText();
                        return JsonSerializer.Deserialize(jsonText, targetType, options);
                    }
                    }

                    // No type metadata or type not found - return as JsonElement for backward compatibility
                    return root.Clone();
                }
            }

            // For primitives, deserialize normally
            return JsonSerializer.Deserialize<JsonElement>(ref reader, options);
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            Type valueType = value.GetType();

            // For primitives and well-known types, serialize normally without type metadata
            if (IsPrimitiveOrWellKnownType(valueType))
            {
                JsonSerializer.Serialize(writer, value, valueType, options);
                return;
            }

            // For complex objects, wrap with type metadata
            writer.WriteStartObject();
            writer.WriteString(TypePropertyName, valueType.AssemblyQualifiedName ?? valueType.FullName ?? valueType.Name);
            writer.WritePropertyName(ValuePropertyName);
            JsonSerializer.Serialize(writer, value, valueType, options);
            writer.WriteEndObject();
        }

        /// <summary>
        /// Determines if a type is a primitive or well-known type that doesn't need type metadata.
        /// </summary>
        static bool IsPrimitiveOrWellKnownType(Type type)
        {
            // Handle nullable types
            Type underlyingType = Nullable.GetUnderlyingType(type) ?? type;

            // Primitives
            if (underlyingType.IsPrimitive)
            {
                return true;
            }

            // Well-known value types
            if (underlyingType == typeof(string) ||
                underlyingType == typeof(DateTime) ||
                underlyingType == typeof(DateTimeOffset) ||
                underlyingType == typeof(TimeSpan) ||
                underlyingType == typeof(Guid) ||
                underlyingType == typeof(decimal) ||
                underlyingType == typeof(Uri))
            {
                return true;
            }

            // JsonElement and JsonNode (already JSON types, no need to wrap)
            if (underlyingType == typeof(JsonElement))
            {
                return true;
            }

            // JsonNode is a special type from System.Text.Json that represents JSON values
            // It should be serialized/deserialized directly without type metadata wrapping
            string? typeName = underlyingType.FullName;
            if (typeName != null && (typeName == "System.Text.Json.Nodes.JsonNode" ||
                                     typeName == "System.Text.Json.Nodes.JsonObject" ||
                                     typeName == "System.Text.Json.Nodes.JsonArray" ||
                                     typeName == "System.Text.Json.Nodes.JsonValue"))
            {
                return true;
            }

            // Record types - records are value-like types used for data transfer
            // They should be serialized without type metadata wrapping for compatibility
            // Check if the type is a record by looking for the compiler-generated EqualityContract property
            // Records are sealed classes with EqualityContract property
            if (underlyingType.IsClass && !underlyingType.IsAbstract)
            {
                PropertyInfo? equalityContract = underlyingType.GetProperty("EqualityContract", BindingFlags.NonPublic | BindingFlags.Instance);
                if (equalityContract != null && equalityContract.PropertyType == typeof(Type))
                {
                    // This is likely a record type - don't wrap with type metadata
                    return true;
                }
            }

            // Arrays of primitives
            if (underlyingType.IsArray)
            {
                Type elementType = underlyingType.GetElementType()!;
                // Don't wrap object[] arrays - they're often used as generic containers
                // and the raw JSON format is more useful and compatible
                if (elementType == typeof(object))
                {
                    return true;
                }
                return IsPrimitiveOrWellKnownType(elementType);
            }

            return false;
        }
    }

    /// <summary>
    /// Converter for Dictionary&lt;string, object&gt; that preserves type information for values.
    /// </summary>
    sealed class DictionaryStringObjectJsonConverter : JsonConverter<Dictionary<string, object>>
    {
        const string TypePropertyName = "$type";
        const string ValuePropertyName = "$value";

        /// <inheritdoc/>
        public override Dictionary<string, object>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException($"Expected StartObject, got {reader.TokenType}");
            }

            // Parse the entire object to check if it's wrapped with type metadata
            using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
            {
                JsonElement root = doc.RootElement;

                // Check if this is a wrapped object with $type metadata
                // This can happen if the dictionary itself was serialized as an object value
                if (root.TryGetProperty("$type", out JsonElement typeElement) &&
                    root.TryGetProperty("$value", out JsonElement valueElement))
                {
                    // This is a wrapped dictionary - deserialize the $value
                    string valueJsonText = valueElement.GetRawText();
                    return JsonSerializer.Deserialize<Dictionary<string, object>>(valueJsonText, options);
                }

                // Not wrapped - this is a regular dictionary, deserialize it from the JsonElement
                Dictionary<string, object> dictionary = new();

                foreach (JsonProperty property in root.EnumerateObject())
                {
                    // Deserialize the value - this will use our ObjectJsonConverter for object types
                    object? value = JsonSerializer.Deserialize<object>(property.Value.GetRawText(), options);
                    dictionary[property.Name] = value ?? null!;
                }

                return dictionary;
            }
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, Dictionary<string, object> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            foreach (KeyValuePair<string, object> kvp in value)
            {
                writer.WritePropertyName(kvp.Key);

                if (kvp.Value == null)
                {
                    writer.WriteNullValue();
                    continue;
                }

                Type valueType = kvp.Value.GetType();
                
                // For dictionary values, we need to wrap records and complex types with type metadata
                // even though ObjectJsonConverter treats records as well-known types
                // This ensures proper deserialization when reading from Dictionary<string, object>
                if (ShouldWrapForDictionary(valueType))
                {
                    // Wrap with type metadata
                    writer.WriteStartObject();
                    writer.WriteString("$type", valueType.AssemblyQualifiedName ?? valueType.FullName ?? valueType.Name);
                    writer.WritePropertyName("$value");
                    JsonSerializer.Serialize(writer, kvp.Value, valueType, options);
                    writer.WriteEndObject();
                }
                else
                {
                    // For primitives and well-known types, serialize normally
                    JsonSerializer.Serialize(writer, kvp.Value, valueType, options);
                }
            }

            writer.WriteEndObject();
        }

        /// <summary>
        /// Determines if a type should be wrapped with type metadata when serialized as a dictionary value.
        /// This is more aggressive than IsPrimitiveOrWellKnownType - we wrap records here even though
        /// they're treated as well-known types in direct serialization.
        /// </summary>
        static bool ShouldWrapForDictionary(Type type)
        {
            Type underlyingType = Nullable.GetUnderlyingType(type) ?? type;

            // Primitives - don't wrap
            if (underlyingType.IsPrimitive)
            {
                return false;
            }

            // Well-known value types - don't wrap
            if (underlyingType == typeof(string) ||
                underlyingType == typeof(DateTime) ||
                underlyingType == typeof(DateTimeOffset) ||
                underlyingType == typeof(TimeSpan) ||
                underlyingType == typeof(Guid) ||
                underlyingType == typeof(decimal) ||
                underlyingType == typeof(Uri))
            {
                return false;
            }

            // JsonElement and JsonNode - don't wrap
            if (underlyingType == typeof(JsonElement))
            {
                return false;
            }

            string? typeName = underlyingType.FullName;
            if (typeName != null && (typeName == "System.Text.Json.Nodes.JsonNode" ||
                                     typeName == "System.Text.Json.Nodes.JsonObject" ||
                                     typeName == "System.Text.Json.Nodes.JsonArray" ||
                                     typeName == "System.Text.Json.Nodes.JsonValue"))
            {
                return false;
            }

            // Arrays of primitives - don't wrap
            if (underlyingType.IsArray)
            {
                Type elementType = underlyingType.GetElementType()!;
                if (elementType == typeof(object))
                {
                    return false;
                }
                return ShouldWrapForDictionary(elementType);
            }

            // Everything else (including records) should be wrapped for dictionary values
            return true;
        }
    }
}
