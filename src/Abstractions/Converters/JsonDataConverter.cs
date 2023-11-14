// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;

namespace Microsoft.DurableTask.Converters;

/// <summary>
/// An implementation of <see cref="DataConverter"/> that uses System.Text.Json APIs for data serialization.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="JsonDataConverter"/> class.
/// </remarks>
/// <param name="options">The serializer options.</param>
public class JsonDataConverter(JsonSerializerOptions? options = null) : DataConverter
{
    // WARNING: Changing default serialization options could potentially be breaking for in-flight orchestrations.
    static readonly JsonSerializerOptions DefaultOptions = new()
    {
        IncludeFields = true,
    };

    readonly JsonSerializerOptions? options = options ?? DefaultOptions;

    /// <summary>
    /// Gets an instance of the <see cref="JsonDataConverter"/> with default configuration.
    /// </summary>
    public static JsonDataConverter Default { get; } = new JsonDataConverter();

    /// <inheritdoc/>
    public override string? Serialize(object? value)
    {
        return value != null ? JsonSerializer.Serialize(value, this.options) : null;
    }

    /// <inheritdoc/>
    public override object? Deserialize(string? data, Type targetType)
    {
        return data != null ? JsonSerializer.Deserialize(data, targetType, this.options) : null;
    }
}
