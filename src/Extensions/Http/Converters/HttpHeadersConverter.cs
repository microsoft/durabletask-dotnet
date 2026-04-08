// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.DurableTask.Http.Converters;

/// <summary>
/// JSON converter for HTTP header dictionaries. Handles both single-value strings and
/// string arrays (joins with ", " to match HTTP header semantics).
/// Returns <c>null</c> when the JSON value is <c>null</c> so callers can distinguish
/// null headers from empty headers.
/// </summary>
internal sealed class HttpHeadersConverter : JsonConverter<IDictionary<string, string>?>
{
    /// <inheritdoc/>
    public override IDictionary<string, string>? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

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
                var values = new List<string>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    string? val = reader.GetString();
                    if (val != null)
                    {
                        values.Add(val);
                    }
                }

                if (values.Count > 0)
                {
                    headers[propertyName] = string.Join(", ", values);
                }
            }
        }

        return headers;
    }

    /// <inheritdoc/>
    public override void Write(
        Utf8JsonWriter writer, IDictionary<string, string>? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();

        foreach (KeyValuePair<string, string> pair in value)
        {
            writer.WriteString(pair.Key, pair.Value);
        }

        writer.WriteEndObject();
    }
}
