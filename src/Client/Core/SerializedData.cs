// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Converters;

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Gets a type representing serialized data.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="SerializedData"/> class.
/// </remarks>
/// <param name="data">The serialized data.</param>
/// <param name="converter">The data converter.</param>
public sealed class SerializedData(string data, DataConverter? converter = null)
{
    /// <summary>
    /// Gets the serialized value.
    /// </summary>
    public string Value { get; } = Check.NotNull(data);

    /// <summary>
    /// Gets a value indicating whether large payload support is enabled.
    /// </summary>
    public bool EnableLargePayloadSupport { get; init; }

    /// <summary>
    /// Gets the data converter.
    /// </summary>
    public DataConverter Converter { get; } = converter ?? JsonDataConverter.Default;

    /// <summary>
    /// Deserializes the data into <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type to deserialize into.</typeparam>
    /// <returns>The deserialized type.</returns>
    public T ReadAs<T>()
    {
        Check.ThrowIfLargePayloadEnabled(this.EnableLargePayloadSupport, nameof(this.ReadAs));
        return this.Converter.Deserialize<T>(this.Value);
    }

    /// <summary>
    /// Deserializes the data into <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type to deserialize into.</typeparam>
    /// <returns>The deserialized type.</returns>
    public async Task<T> ReadAsAsync<T>() => await this.Converter.DeserializeAsync<T>(this.Value);

    /// <summary>
    /// Creates a new instance of <see cref="SerializedData"/> from the specified data.
    /// </summary>
    /// <param name="data">The data to serialize.</param>
    /// <param name="converter">The data converter.</param>
    /// <param name="enableLargePayloadSupport">Whether to use async serialization for large payloads.</param>
    /// <returns>Serialized data.</returns>
    internal static SerializedData Create(object data, DataConverter? converter = null, bool enableLargePayloadSupport = false)
    {
        converter ??= JsonDataConverter.Default;
        return new SerializedData(converter.Serialize(data), converter)
        {
            EnableLargePayloadSupport = enableLargePayloadSupport,
        };
    }

    /// <summary>
    /// Creates a new instance of <see cref="SerializedData"/> from the specified data.
    /// </summary>
    /// <param name="data">The data to serialize.</param>
    /// <param name="converter">The data converter.</param>
    /// <param name="enableLargePayloadSupport">Whether to use async serialization for large payloads.</param>
    /// <returns>Serialized data.</returns>
    internal static async Task<SerializedData> CreateAsync(object data, DataConverter converter, bool enableLargePayloadSupport = false)
    {
        return new SerializedData(await converter.SerializeAsync(data), converter)
        {
            EnableLargePayloadSupport = enableLargePayloadSupport,
        };
    }
}
