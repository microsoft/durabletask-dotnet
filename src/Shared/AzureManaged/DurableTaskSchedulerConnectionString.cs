// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Data.Common;

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

    string? AdditionallyAllowedTenantsStr => this.GetValue("AdditionallyAllowedTenants");

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
