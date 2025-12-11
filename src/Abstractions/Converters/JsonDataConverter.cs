// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.DurableTask.Converters;

/// <summary>
/// An implementation of <see cref="DataConverter"/> that uses System.Text.Json APIs for data serialization.
/// </summary>
public class JsonDataConverter : DataConverter
{
    // WARNING: Changing default serialization options could potentially be breaking for in-flight orchestrations.
    static readonly JsonSerializerOptions DefaultOptions = CreateDefaultOptions();

    readonly JsonSerializerOptions? options;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonDataConverter"/> class.
    /// </summary>
    /// <param name="options">The serializer options. If null, default options with type metadata preservation are used.</param>
    public JsonDataConverter(JsonSerializerOptions? options = null)
    {
        if (options != null)
        {
            // Ensure the ObjectWithTypeMetadataJsonConverterFactory is present in custom options
            // Check if it's already there to avoid duplicates
            bool hasConverter = false;
            foreach (JsonConverter converter in options.Converters)
            {
                if (converter is ObjectWithTypeMetadataJsonConverterFactory)
                {
                    hasConverter = true;
                    break;
                }
            }

            if (!hasConverter)
            {
                // Add at the beginning to ensure it handles object types first
                options.Converters.Insert(0, new ObjectWithTypeMetadataJsonConverterFactory());
            }
        }

        this.options = options ?? DefaultOptions;
    }

    /// <summary>
    /// Gets an instance of the <see cref="JsonDataConverter"/> with default configuration.
    /// </summary>
    public static JsonDataConverter Default { get; } = new JsonDataConverter();

    static JsonSerializerOptions CreateDefaultOptions()
    {
        JsonSerializerOptions options = new()
        {
            IncludeFields = true,
        };

        // Add converter factory for preserving type information when deserializing to object type
        // and Dictionary<string, object> values. This must be added early to handle object types
        // before default JsonElement conversion.
        // See issue #430: https://github.com/microsoft/durabletask-dotnet/issues/430
        options.Converters.Insert(0, new ObjectWithTypeMetadataJsonConverterFactory());

        return options;
    }

    /// <inheritdoc/>
    public override string? Serialize(object? value)
    {
        return value != null ? JsonSerializer.Serialize(value, this.options) : null;
    }

    /// <inheritdoc/>
    public override object? Deserialize(string? data, Type targetType)
    {
        if (data == null)
        {
            return null;
        }

        // Special case: If target type is JsonElement, we should unwrap any type metadata
        // and return the actual JSON content, not the wrapped structure
        if (targetType == typeof(JsonElement))
        {
            using (JsonDocument doc = JsonDocument.Parse(data))
            {
                JsonElement root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("$type", out JsonElement typeElement) &&
                    root.TryGetProperty("$value", out JsonElement valueElement))
                {
                    // Unwrap and return the actual value as JsonElement
                    return valueElement.Clone();
                }
            }
        }

        // Check if the JSON is a wrapped object (has $type and $value)
        // This handles both array wrappers and object wrappers
        using (JsonDocument doc = JsonDocument.Parse(data))
        {
            JsonElement root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("$type", out JsonElement typeElement) &&
                root.TryGetProperty("$value", out JsonElement valueElement))
            {
                // This is a wrapped object - check what type it is
                string typeName = typeElement.GetString()!;
                Type? wrappedType = Type.GetType(typeName, throwOnError: false);

                // If the wrapped type matches the target type exactly, unwrap and deserialize
                if (wrappedType != null && wrappedType == targetType)
                {
                    // The wrapped type matches the target type - unwrap and deserialize
                    string unwrappedJson = valueElement.GetRawText();
                    return JsonSerializer.Deserialize(unwrappedJson, targetType, this.options);
                }

                // Special case: If wrapped type is an array and target type is also an array,
                // try to deserialize the entire array (for cases like int[] -> int[])
                if (wrappedType != null && wrappedType.IsArray && targetType.IsArray)
                {
                    string unwrappedJson = valueElement.GetRawText();
                    return JsonSerializer.Deserialize(unwrappedJson, targetType, this.options);
                }
                
                // Special case: If wrapped type is a collection (like List<T>) and target type is an array (like T[]),
                // deserialize the $value JSON directly as the target array type
                // This works because List<T> and T[] have the same JSON representation: [item1, item2, ...]
                if (wrappedType != null && targetType.IsArray && valueElement.ValueKind == JsonValueKind.Array)
                {
                    // Check if wrapped type is a generic collection that implements IEnumerable<T>
                    if (wrappedType.IsGenericType)
                    {
                        Type genericTypeDef = wrappedType.GetGenericTypeDefinition();
                        if (genericTypeDef == typeof(List<>) || 
                            genericTypeDef == typeof(IList<>) ||
                            genericTypeDef == typeof(ICollection<>) ||
                            genericTypeDef == typeof(IEnumerable<>))
                        {
                            Type[] genericArgs = wrappedType.GetGenericArguments();
                            if (genericArgs.Length == 1)
                            {
                                Type elementType = genericArgs[0];
                                Type targetElementType = targetType.GetElementType()!;
                                
                                // If the element types match, we can deserialize directly
                                if (elementType == targetElementType)
                                {
                                    string unwrappedJson = valueElement.GetRawText();
                                    return JsonSerializer.Deserialize(unwrappedJson, targetType, this.options);
                                }
                            }
                        }
                    }
                }
                
                // Special case: If wrapped type is an array and target type is NOT an array,
                // check if the array contains a single wrapped element that matches the target type
                if (wrappedType != null && wrappedType.IsArray && !targetType.IsArray)
                {
                    if (valueElement.ValueKind == JsonValueKind.Array && valueElement.GetArrayLength() > 0)
                    {
                        // Get the first element of the array
                        JsonElement firstElement = valueElement[0];
                        string firstElementJson = firstElement.GetRawText();
                        
                        // Check if the first element is also wrapped
                        using (JsonDocument innerDoc = JsonDocument.Parse(firstElementJson))
                        {
                            JsonElement innerRoot = innerDoc.RootElement;
                            if (innerRoot.ValueKind == JsonValueKind.Object &&
                                innerRoot.TryGetProperty("$type", out JsonElement innerTypeElement) &&
                                innerRoot.TryGetProperty("$value", out JsonElement innerValueElement))
                            {
                                string innerTypeName = innerTypeElement.GetString()!;
                                Type? innerWrappedType = Type.GetType(innerTypeName, throwOnError: false);
                                
                                if (innerWrappedType != null && innerWrappedType == targetType)
                                {
                                    // Unwrap the inner object
                                    string unwrappedJson = innerValueElement.GetRawText();
                                    return JsonSerializer.Deserialize(unwrappedJson, targetType, this.options);
                                }
                            }
                        }
                        
                        // First element is not wrapped, try to deserialize it directly
                        // This handles cases where the array contains a single unwrapped element
                        try
                        {
                            return JsonSerializer.Deserialize(firstElementJson, targetType, this.options);
                        }
                        catch
                        {
                            // If deserialization fails, fall through to normal deserialization
                        }
                    }
                }
            }
        }

        // Not wrapped or type doesn't match - deserialize normally
        return JsonSerializer.Deserialize(data, targetType, this.options);
    }
}
