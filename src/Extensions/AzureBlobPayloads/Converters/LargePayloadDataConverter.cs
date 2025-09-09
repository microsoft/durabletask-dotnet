// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Microsoft.DurableTask.Converters;

/// <summary>
/// A DataConverter that wraps another DataConverter and externalizes payloads larger than a configured threshold.
/// It uploads large payloads to an <see cref="IPayloadStore"/> and returns a reference token string.
/// On deserialization, it resolves tokens and feeds the underlying converter the original content.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="LargePayloadDataConverter"/> class.
/// </remarks>
/// <param name="innerConverter">The inner data converter to wrap.</param>
/// <param name="payloadStore">The external payload store to use.</param>
/// <param name="largePayloadStorageOptions">The options for the externalizing data converter.</param>
/// <exception cref="ArgumentNullException">Thrown when <paramref name="innerConverter"/>, <paramref name="payloadStore"/>, or <paramref name="largePayloadStorageOptions"/> is null.</exception>
public sealed class LargePayloadDataConverter(
    DataConverter innerConverter,
    IPayloadStore payloadStore,
    LargePayloadStorageOptions largePayloadStorageOptions) : DataConverter
{
    readonly DataConverter innerConverter = innerConverter ?? throw new ArgumentNullException(nameof(innerConverter));
    readonly IPayloadStore payLoadStore = payloadStore ?? throw new ArgumentNullException(nameof(payloadStore));
    readonly LargePayloadStorageOptions largePayloadStorageOptions = largePayloadStorageOptions ?? throw new ArgumentNullException(nameof(largePayloadStorageOptions));

    // Use UTF-8 without a BOM (encoderShouldEmitUTF8Identifier=false). JSON in UTF-8 should not include a
    // byte order mark per RFC 8259, and omitting it avoids hidden extra bytes that could skew the
    // externalization threshold calculation and prevents interop issues with strict JSON parsers.
    // A few legacy tools rely on a BOM for encoding detection, but modern JSON tooling assumes BOM-less UTF-8.
    readonly Encoding utf8 = new UTF8Encoding(false);

    /// <inheritdoc/>
    [return: NotNullIfNotNull("value")]
    public override string? Serialize(object? value)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    [return: NotNullIfNotNull("data")]
    public override object? Deserialize(string? data, Type targetType)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Serializes the value to a JSON string and uploads it to the external payload store if it exceeds the configured threshold.
    /// </summary>
    /// <param name="value">The value to serialize.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The serialized value or the token if externalized.</returns>
    public override async ValueTask<string?> SerializeAsync(object? value, CancellationToken cancellationToken = default)
    {
        string? json = this.innerConverter.Serialize(value);

        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        int byteCount = this.utf8.GetByteCount(json);
        if (byteCount < this.largePayloadStorageOptions.ExternalizeThresholdBytes)
        {
            return json;
        }

        // Upload synchronously in this context by blocking on async. SDK call sites already run on threadpool.
        byte[] bytes = this.utf8.GetBytes(json);
        return await this.payLoadStore.UploadAsync(bytes, cancellationToken);
    }

    /// <summary>
    /// Deserializes the JSON string or resolves the token to the original value.
    /// </summary>
    /// <param name="data">The JSON string or token.</param>
    /// <param name="targetType">The type to deserialize to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized value.</returns>
    public override async ValueTask<object?> DeserializeAsync(
        string? data,
        Type targetType,
        CancellationToken cancellationToken = default)
    {
        if (data is null)
        {
            return null;
        }

        string toDeserialize = data;
        if (this.payLoadStore.IsKnownPayloadToken(data))
        {
            toDeserialize = await this.payLoadStore.DownloadAsync(data, CancellationToken.None);
        }

        return this.innerConverter.Deserialize(StripArrayCharacters(toDeserialize), targetType);
    }

    static string? StripArrayCharacters(string? input)
    {
        if (input != null && input.StartsWith('[') && input.EndsWith(']'))
        {
            // Strip the outer bracket characters
            return input[1..^1];
        }

        return input;
    }
}
