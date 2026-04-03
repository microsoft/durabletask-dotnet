// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.DurableTask.Http.Converters;

/// <summary>
/// JSON converter for <see cref="HttpMethod"/>.
/// </summary>
internal sealed class HttpMethodConverter : JsonConverter<HttpMethod>
{
    /// <inheritdoc/>
    public override HttpMethod Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string value = reader.GetString() ?? string.Empty;
        return new HttpMethod(value);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, HttpMethod value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
