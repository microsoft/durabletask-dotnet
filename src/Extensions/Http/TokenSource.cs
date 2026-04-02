// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Microsoft.DurableTask.Http;

/// <summary>
/// Abstract base class for token sources used to authenticate durable HTTP requests.
/// </summary>
/// <remarks>
/// Token acquisition is not supported in standalone mode. TokenSource types are included
/// for wire compatibility with the Azure Functions Durable Task extension. If a TokenSource
/// is set on a request, the built-in HTTP activity will throw <see cref="NotSupportedException"/>.
/// Pass authentication tokens directly via request headers instead.
/// </remarks>
public abstract class TokenSource
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TokenSource"/> class.
    /// </summary>
    /// <param name="resource">The resource identifier.</param>
    internal TokenSource(string resource)
    {
        this.Resource = resource ?? throw new ArgumentNullException(nameof(resource));
    }

    /// <summary>
    /// Gets the resource identifier for the token source.
    /// </summary>
    [JsonPropertyName("resource")]
    public string Resource { get; }
}

/// <summary>
/// Token source implementation for Azure Managed Identities.
/// </summary>
/// <remarks>
/// Included for wire compatibility only. Token acquisition is not supported in standalone mode.
/// </remarks>
public class ManagedIdentityTokenSource : TokenSource
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ManagedIdentityTokenSource"/> class.
    /// </summary>
    /// <param name="resource">The Entra ID resource identifier of the web API being invoked.</param>
    /// <param name="options">Optional Azure credential options.</param>
    public ManagedIdentityTokenSource(string resource, ManagedIdentityOptions? options = null)
        : base(NormalizeResource(resource))
    {
        this.Options = options;
    }

    /// <summary>
    /// Gets the Azure credential options.
    /// </summary>
    [JsonPropertyName("options")]
    public ManagedIdentityOptions? Options { get; }

    static string NormalizeResource(string resource)
    {
        if (resource == null)
        {
            throw new ArgumentNullException(nameof(resource));
        }

        if (resource == "https://management.core.windows.net" || resource == "https://management.core.windows.net/")
        {
            return "https://management.core.windows.net/.default";
        }

        if (resource == "https://graph.microsoft.com" || resource == "https://graph.microsoft.com/")
        {
            return "https://graph.microsoft.com/.default";
        }

        return resource;
    }
}

/// <summary>
/// Configuration options for <see cref="ManagedIdentityTokenSource"/>.
/// </summary>
public class ManagedIdentityOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ManagedIdentityOptions"/> class.
    /// </summary>
    /// <param name="authorityHost">The Entra ID authority host.</param>
    /// <param name="tenantId">The tenant ID.</param>
    public ManagedIdentityOptions(Uri? authorityHost = null, string? tenantId = null)
    {
        this.AuthorityHost = authorityHost;
        this.TenantId = tenantId;
    }

    /// <summary>
    /// Gets or sets the Entra ID authority host.
    /// </summary>
    [JsonPropertyName("authorityhost")]
    public Uri? AuthorityHost { get; set; }

    /// <summary>
    /// Gets or sets the tenant ID.
    /// </summary>
    [JsonPropertyName("tenantid")]
    public string? TenantId { get; set; }
}
