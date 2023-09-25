// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Gets a type representing serialized data.
/// </summary>
public sealed class SerializedData
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SerializedData"/> class.
    /// </summary>
    /// <param name="data">The serialized data.</param>
    /// <param name="converter">The data converter.</param>
    public SerializedData(string data, DataConverter converter)
    {
        this.Value = Check.NotNull(data);
        this.Converter = Check.NotNull(converter);
    }

    /// <summary>
    /// Gets the serialized value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets the data converter.
    /// </summary>
    public DataConverter Converter { get; }

    /// <summary>
    /// Deserializes the data into <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type to deserialize into.</typeparam>
    /// <returns>The deserialized type.</returns>
    public T ReadAs<T>() => this.Converter.Deserialize<T>(this.Value);
}
