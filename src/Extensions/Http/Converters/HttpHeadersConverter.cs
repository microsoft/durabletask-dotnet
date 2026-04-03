// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.DurableTask.Http.Converters;

/// <summary>
/// JSON converter for HTTP header dictionaries. Handles both single-value strings and
/// string arrays (takes the last value for simplicity since <see cref="DurableHttpResponse.Headers"/>
/// is <c>IDictionary&lt;string, string&gt;</c>).
/// </summary>
internal sealed class HttpHeadersConverter : JsonConverter<IDictionary<string, string>>
{
    /// <inheritdoc/>
    public override IDictionary<string, string> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            return headers;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string propertyName = reader.GetString()!;
            reader.Read();

            if (reader.TokenType == JsonTokenType.String)
            {
                headers[propertyName] = reader.GetString()!;
            }
            else if (reader.TokenType == JsonTokenType.StartArray)
            {
                string? lastValue = null;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    lastValue = reader.GetString();
                }

                if (lastValue != null)
                {
                    headers[propertyName] = lastValue;
                }
            }
        }

        return headers;
    }

    /// <inheritdoc/>
    public override void Write(
        Utf8JsonWriter writer, IDictionary<string, string> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (KeyValuePair<string, string> pair in value)
        {
            writer.WriteString(pair.Key, pair.Value);
        }

        writer.WriteEndObject();
    }
}
