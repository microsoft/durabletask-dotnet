// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace Microsoft.DurableTask;

/// <summary>
/// Abstraction for serializing and deserializing data that gets passed to and from orchestrators and activities.
/// </summary>
/// <remarks>
/// Implementations of this abstract class are free to use any serialization method. The default implementation
/// uses the JSON serializer from the System.Text.Json namespace. Currently only strings are supported as
/// the serialized representation of data. Byte array payloads and streams are not supported by this abstraction.
/// Note that these methods all accept null values, in which case the return value should also be null.
/// Implementations may choose to return a pointer or reference (such as an external token) to the data
/// instead of the actual serialized data itself.
/// </remarks>
public abstract class DataConverter
{
    /// <summary>
    /// Serializes <paramref name="value"/> into a text string.
    /// </summary>
    /// <param name="value">The value to be serialized.</param>
    /// <returns>
    /// Returns a text representation of <paramref name="value"/> or <c>null</c> if the input is null.
    /// </returns>
    [return: NotNullIfNotNull("value")]
    public abstract string? Serialize(object? value);

    /// <summary>
    /// Deserializes <paramref name="data"/> into an object of type <paramref name="targetType"/>.
    /// </summary>
    /// <param name="data">The text data to be deserialized.</param>
    /// <param name="targetType">The type to deserialize the text data into.</param>
    /// <returns>
    /// Returns a deserialized object or <c>null</c> if the input is null.
    /// </returns>
    [return: NotNullIfNotNull("data")]
    public abstract object? Deserialize(string? data, Type targetType);

    /// <summary>
    /// Deserializes <paramref name="data"/> into an object of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="data">The text data to be deserialized.</param>
    /// <returns>
    /// Returns a deserialized object or <c>null</c> if the input is null.
    /// </returns>
    [return: NotNullIfNotNull("data")]
    public virtual T? Deserialize<T>(string? data) => (T?)(this.Deserialize(data, typeof(T)) ?? default);

    /// <summary>
    /// Asynchronously serializes <paramref name="value"/> into a text string.
    /// Default implementation delegates to <see cref="Serialize(object?)"/>.
    /// </summary>
    /// <param name="value">The value to be serialized.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task whose result is the serialized string or <c>null</c>.</returns>
    public virtual ValueTask<string?> SerializeAsync(object? value, CancellationToken cancellationToken = default)
    {
        return new ValueTask<string?>(this.Serialize(value));
    }

    /// <summary>
    /// Asynchronously deserializes <paramref name="data"/> into an object of type <paramref name="targetType"/>.
    /// Default implementation delegates to <see cref="Deserialize(string?, Type)"/>.
    /// </summary>
    /// <param name="data">The text data to be deserialized.</param>
    /// <param name="targetType">The type to deserialize to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task whose result is the deserialized value or <c>null</c>.</returns>
    public virtual ValueTask<object?> DeserializeAsync(
        string? data,
        Type targetType,
        CancellationToken cancellationToken = default)
    {
        return new ValueTask<object?>(this.Deserialize(data, targetType));
    }

    /// <summary>
    /// Asynchronously deserializes <paramref name="data"/> into an object of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="data">The text data to be deserialized.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task whose result is the deserialized value or <c>null</c>.</returns>
    public virtual ValueTask<T?> DeserializeAsync<T>(
        string? data,
        CancellationToken cancellationToken = default)
    {
        return new ValueTask<T?>(this.Deserialize<T>(data));
    }
}
