//  ----------------------------------------------------------------------------------
//  Copyright Microsoft Corporation
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//  http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//  ----------------------------------------------------------------------------------

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
