// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.Json;

namespace DurableTask;

/// <summary>
/// An implementation of <see cref="IDataConverter"/> that uses System.Text.Json APIs for data serialization.
/// </summary>
public class JsonDataConverter : IDataConverter
{
    // WARNING: Changing default serialization options could potentially be breaking for in-flight orchestrations.
    static readonly JsonSerializerOptions DefaultOptions = new()
    {
        IncludeFields = true,
    };

    /// <summary>
    /// An instance of the <see cref="JsonDataConverter"/> with default configuration.
    /// </summary>
    public static JsonDataConverter Default { get; } = new JsonDataConverter();

    readonly JsonSerializerOptions? options;

    public JsonDataConverter(JsonSerializerOptions? options = null)
    {
        if (options != null)
        {
            this.options = options;
        }
        else
        {
            this.options = DefaultOptions;
        }
    }

    public virtual string? Serialize(object? value)
    {
        return value != null ? JsonSerializer.Serialize(value, this.options) : null;
    }

    public virtual object? Deserialize(string? data, Type targetType)
    {
        return data != null ? JsonSerializer.Deserialize(data, targetType, this.options) : null;
    }
}
