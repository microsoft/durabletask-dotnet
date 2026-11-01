// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Data.Common;
using Azure.Identity;

namespace Microsoft.DurableTask;

/// <summary>
/// Represents the constituent parts of a connection string for a Durable Task Scheduler service.
/// </summary>
sealed class DurableTaskSchedulerConnectionString
{
    readonly DbConnectionStringBuilder builder;

    /// <summary>
    /// Initializes a new instance of the <see cref="DurableTaskSchedulerConnectionString"/> class.
    /// </summary>
    /// <param name="connectionString">A connection string for a Durable Task Scheduler service.</param>
    public DurableTaskSchedulerConnectionString(string connectionString)
    {
        this.builder = new() { ConnectionString = connectionString };
    }

    /// <summary>
    /// Gets the authentication method specified in the connection string (if any).
    /// </summary>
    public string Authentication => this.GetRequiredValue("Authentication");

    /// <summary>
    /// Gets the managed identity or workload identity client ID specified in the connection string (if any).
    /// </summary>
    public string? ClientId => this.GetValue("ClientID");

    /// <summary>
    /// Gets the "AdditionallyAllowedTenants" property, optionally used by Workload Identity.
    /// Multiple values can be separated by a comma.
    /// </summary>
    public IList<string>? AdditionallyAllowedTenants =>
        string.IsNullOrEmpty(this.AdditionallyAllowedTenantsStr)
            ? null
            : this.AdditionallyAllowedTenantsStr!.Split(',');

    /// <summary>
    /// Gets the "TenantId" property, optionally used by Workload Identity.
    /// </summary>
    public string? TenantId => this.GetValue("TenantId");

    /// <summary>
    /// Gets the "TokenFilePath" property, optionally used by Workload Identity.
    /// </summary>
    public string? TokenFilePath => this.GetValue("TokenFilePath");

    /// <summary>
    /// Gets the endpoint specified in the connection string (if any).
    /// </summary>
    public string Endpoint => this.GetRequiredValue("Endpoint");

    /// <summary>
    /// Gets the task hub name specified in the connection string.
    /// </summary>
    public string TaskHubName => this.GetRequiredValue("TaskHub");

    /// <summary>
    /// Gets the optional token audience URI. Normalization is performed by the scheduler options.
    /// </summary>
    public string? ResourceId => this.GetValue("ResourceId");

    string? AdditionallyAllowedTenantsStr => this.GetValue("AdditionallyAllowedTenants");

    /// <summary>
    /// Creates credential options, forwarding an explicit authority only when supplied.
    /// </summary>
    /// <typeparam name="TOptions">The Azure Identity options type.</typeparam>
    /// <returns>Options for a credential that supports authority configuration.</returns>
    public TOptions CreateCredentialOptions<TOptions>()
        where TOptions : TokenCredentialOptions, new()
    {
        TOptions options = new();
        string? authorityHost = this.GetValue("AuthorityHost");
        if (!string.IsNullOrEmpty(authorityHost))
        {
            if (!Uri.TryCreate(authorityHost, UriKind.Absolute, out Uri? authority)
                || authority.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException(
                    "The connection string AuthorityHost must be an absolute HTTPS URI, such as https://login.microsoftonline.us/.",
                    "connectionString");
            }

            options.AuthorityHost = authority;
        }

        return options;
    }

    string? GetValue(string name) =>
        this.builder.TryGetValue(name, out object? value)
            ? value as string
            : null;

    string GetRequiredValue(string name)
    {
        string? value = this.GetValue(name);
        return Check.NotNullOrEmpty(value, name);
    }
}
