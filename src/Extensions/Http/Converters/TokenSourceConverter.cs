// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.DurableTask.Http.Converters;

/// <summary>
/// JSON converter for <see cref="TokenSource"/> — wire compatibility only.
/// Deserializes managed identity token source payloads so that JSON from the Azure Functions
/// extension round-trips without parse errors. Token acquisition is <b>not supported</b>
/// in standalone mode — <see cref="BuiltInHttpActivity"/> will throw
/// <see cref="NotSupportedException"/> at runtime if a <see cref="TokenSource"/> is present.
/// Unrecognized payloads deserialize as <c>null</c>.
/// </summary>
internal sealed class TokenSourceConverter : JsonConverter<TokenSource>
{
    /// <inheritdoc/>
    public override TokenSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Deserialize for wire-compat; the activity will reject non-null TokenSource at runtime.
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
