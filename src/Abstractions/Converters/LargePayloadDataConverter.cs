// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
public sealed class LargePayloadDataConverter(DataConverter innerConverter, IPayloadStore payloadStore, LargePayloadStorageOptions largePayloadStorageOptions) : DataConverter
{
    const string TokenPrefix = "blob:v1:"; // matches BlobExternalPayloadStore

    readonly DataConverter innerConverter = innerConverter ?? throw new ArgumentNullException(nameof(innerConverter));
    readonly IPayloadStore payLoadStore = payloadStore ?? throw new ArgumentNullException(nameof(payloadStore));
    readonly LargePayloadStorageOptions largePayloadStorageOptions = largePayloadStorageOptions ?? throw new ArgumentNullException(nameof(largePayloadStorageOptions));
    readonly Encoding utf8 = new UTF8Encoding(false);

    /// <inheritdoc/>
    public override bool UsesExternalStorage => true;

    /// <summary>
    /// Serializes the value to a JSON string and uploads it to the external payload store if it exceeds the configured threshold.
    /// </summary>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The serialized value or the token if externalized.</returns>
    public override string? Serialize(object? value)
    {
        if (value is null)
        {
            return null;
        }

        string json = this.innerConverter.Serialize(value) ?? "null";

        int byteCount = this.utf8.GetByteCount(json);
        if (byteCount < this.largePayloadStorageOptions.ExternalizeThresholdBytes)
        {
            return json;
        }

        // Upload synchronously in this context by blocking on async. SDK call sites already run on threadpool.
        byte[] bytes = this.utf8.GetBytes(json);
        string token = this.payLoadStore.UploadAsync(bytes, CancellationToken.None).GetAwaiter().GetResult();
        return token;
    }

    /// <summary>
    /// Deserializes the JSON string or resolves the token to the original value.
    /// </summary>
    /// <param name="data">The JSON string or token.</param>
    /// <param name="targetType">The type to deserialize to.</param>
    /// <returns>The deserialized value.</returns>
    public override object? Deserialize(string? data, Type targetType)
    {
        if (data is null)
        {
            return null;
        }

        string toDeserialize = data;
        if (data.StartsWith(TokenPrefix, StringComparison.Ordinal))
        {
            toDeserialize = this.payLoadStore.DownloadAsync(data, CancellationToken.None).GetAwaiter().GetResult();
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
