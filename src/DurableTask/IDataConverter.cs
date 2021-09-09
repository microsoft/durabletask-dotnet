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
using System.Diagnostics.CodeAnalysis;

namespace DurableTask;

/// <summary>
/// Interface for serializing and deserializing data that gets passed to and from orchestrators and activities.
/// </summary>
/// <remarks>
/// Implementations of this interface are free to use any serialization method. The default implementation
/// uses the JSON serializer from the System.Text.Json namespace. Currently only strings are supported as
/// the serialized representation of data. Byte array payloads and streams are not supported by this interface.
/// Note that these methods all accept null values, in which case the return value should also be null.
/// </remarks>
public interface IDataConverter
{
    /// <summary>
    /// Serializes <paramref name="value"/> into a text string.
    /// </summary>
    /// <param name="value">The value to be serialized.</param>
    /// <returns>
    /// Returns a serialized <c>string</c> representation of <paramref name="value"/> or <c>null</c> if the input is null.
    /// </returns>
    [return: NotNullIfNotNull("value")]
    string? Serialize(object? value);

    /// <summary>
    /// Deserializes <paramref name="data"/> into an object of type <paramref name="targetType"/>.
    /// </summary>
    /// <param name="data">The text data to be deserialized.</param>
    /// <returns>
    /// Returns a deserialized object or <c>null</c> if the input is null.
    /// </returns>
    [return: NotNullIfNotNull("data")]
    object? Deserialize(string? data, Type targetType);

    /// <summary>
    /// Deserializes <paramref name="data"/> into an object of type <typeparamref name="T"/>.
    /// </summary>
    /// <param name="data">The text data to be deserialized.</param>
    /// <returns>
    /// Returns a deserialized object or <c>null</c> if the input is null.
    /// </returns>
    [return: NotNullIfNotNull("data")]
    T? Deserialize<T>(string? data) => (T?)(this.Deserialize(data, typeof(T)) ?? default);
}
