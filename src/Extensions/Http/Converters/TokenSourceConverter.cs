// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.DurableTask.Http.Converters;

/// <summary>
/// JSON converter for <see cref="TokenSource"/> — handles serialization only.
/// Deserialization is not supported since token acquisition is not available in standalone mode.
/// </summary>
internal sealed class TokenSourceConverter : JsonConverter<TokenSource>
{
    /// <inheritdoc/>
    public override TokenSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Skip the token source object during deserialization — token acquisition is not supported.
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;

        if (root.TryGetProperty("resource", out JsonElement resourceElement))
        {
            string resource = resourceElement.GetString() ?? string.Empty;
            ManagedIdentityOptions? opts = null;

            if (root.TryGetProperty("options", out JsonElement optionsElement))
            {
                Uri? authorityHost = null;
                string? tenantId = null;

                if (optionsElement.TryGetProperty("authorityhost", out JsonElement authHostElement))
                {
                    string? authHostStr = authHostElement.GetString();
                    if (authHostStr != null)
                    {
                        authorityHost = new Uri(authHostStr);
                    }
                }

                if (optionsElement.TryGetProperty("tenantid", out JsonElement tenantElement))
                {
                    tenantId = tenantElement.GetString();
                }

                opts = new ManagedIdentityOptions(authorityHost, tenantId);
            }

            return new ManagedIdentityTokenSource(resource, opts);
        }

        return null;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, TokenSource value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("kind", "AzureManagedIdentity");
        writer.WriteString("resource", value.Resource);

        if (value is ManagedIdentityTokenSource managedIdentity && managedIdentity.Options != null)
        {
            writer.WritePropertyName("options");
            JsonSerializer.Serialize(writer, managedIdentity.Options, options);
        }

        writer.WriteEndObject();
    }
}
