﻿﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;
using Azure.Core;
using Azure.Identity;

namespace Microsoft.DurableTask.Extensions.Azure;

/// <summary>
/// Options for configuring the Durable Task Scheduler.
/// </summary>
public class DurableTaskSchedulerOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DurableTaskSchedulerOptions"/> class.
    /// </summary>
    /// <param name="endpointAddress">The endpoint address of the Durable Task Scheduler service.</param>
    /// <param name="taskHubName">The name of the task hub to connect to.</param>
    /// <param name="credential">The credential to use for authentication, or null for no authentication.</param>
    internal DurableTaskSchedulerOptions(string endpointAddress, string taskHubName, TokenCredential? credential = null)
    {
        this.EndpointAddress = endpointAddress ?? throw new ArgumentNullException(nameof(endpointAddress));
        this.TaskHubName = taskHubName ?? throw new ArgumentNullException(nameof(taskHubName));
        this.Credential = credential;
    }

    /// <summary>
    /// Gets the endpoint address of the Durable Task Scheduler resource.
    /// Expected to be in the format "https://{scheduler-name}.{region}.durabletask.io".
    /// </summary>
    public string EndpointAddress { get; }

    /// <summary>
    /// Gets the name of the task hub resource associated with the Durable Task Scheduler resource.
    /// </summary>
    public string TaskHubName { get; }

    /// <summary>
    /// Gets the credential used to authenticate with the Durable Task Scheduler task hub resource.
    /// </summary>
    public TokenCredential? Credential { get; }

    /// <summary>
    /// Gets or sets the resource ID of the Durable Task Scheduler resource.
    /// The default value is https://durabletask.io.
    /// </summary>
    public string? ResourceId { get; set; }

    /// <summary>
    /// Gets or sets the worker ID used to identify the worker instance.
    /// The default value is a string containing the machine name and the process ID.
    /// </summary>
    public string? WorkerId { get; set; }

    /// <summary>
    /// Creates a new instance of <see cref="DurableTaskSchedulerOptions"/> from a connection string.
    /// </summary>
    /// <param name="connectionString">The connection string containing the configuration settings.</param>
    /// <returns>A new instance of <see cref="DurableTaskSchedulerOptions"/> configured with the connection string settings.</returns>
    /// <exception cref="ArgumentException">Thrown when the connection string contains an unsupported authentication type.</exception>
    public static DurableTaskSchedulerOptions FromConnectionString(string connectionString)
    {
        return FromConnectionString(new DurableTaskSchedulerConnectionString(connectionString));
    }

    /// <summary>
    /// Creates a new instance of <see cref="DurableTaskSchedulerOptions"/> from a parsed connection string.
    /// </summary>
    /// <param name="connectionString">The parsed connection string containing the configuration settings.</param>
    /// <returns>A new instance of <see cref="DurableTaskSchedulerOptions"/> configured with the connection string settings.</returns>
    /// <exception cref="ArgumentException">Thrown when the connection string contains an unsupported authentication type.</exception>
    public static DurableTaskSchedulerOptions FromConnectionString(
        DurableTaskSchedulerConnectionString connectionString)
    {
        // Example connection strings:
        // "Endpoint=https://myaccount.westus3.durabletask.io/;Authentication=ManagedIdentity;ClientID=00000000-0000-0000-0000-000000000000;TaskHubName=th01"
        // "Endpoint=https://myaccount.westus3.durabletask.io/;Authentication=DefaultAzure;TaskHubName=th01"
        // "Endpoint=https://myaccount.westus3.durabletask.io/;Authentication=None;TaskHubName=th01" (undocumented and only intended for local testing)
        string endpointAddress = connectionString.Endpoint;

        if (!endpointAddress.Contains("://"))
        {
            // If the protocol is missing, assume HTTPS.
            endpointAddress = "https://" + endpointAddress;
        }

        string authType = connectionString.Authentication;
        TokenCredential? credential;

        // Parse the supported auth types, in a case-insensitive way and without spaces
        switch (authType.ToLower(CultureInfo.InvariantCulture).Replace(" ", string.Empty))
        {
            case "defaultazure":
                // Default Azure credentials, suitable for a variety of scenarios
                // In many cases, users will need to pass additional configuration options via env vars
                credential = new DefaultAzureCredential();
                break;

            case "managedidentity":
                // Use Managed identity
                // Suitable for Azure-hosted scenarios
                // Note that ClientId could be null for system-assigned managed identities
                credential = new ManagedIdentityCredential(connectionString.ClientId);
                break;

            case "workloadidentity":
                // Use Workload Identity Federation.
                // This is commonly-used in Kubernetes (hosted on Azure or anywhere), or in CI environments like
                // Azure Pipelines or GitHub Actions. It can also be used with SPIFFE.
                WorkloadIdentityCredentialOptions opts = new() { };
                if (!string.IsNullOrEmpty(connectionString.ClientId))
                {
                    opts.ClientId = connectionString.ClientId;
                }

                if (!string.IsNullOrEmpty(connectionString.TenantId))
                {
                    opts.TenantId = connectionString.TenantId;
                }

                if (connectionString.AdditionallyAllowedTenants is not null)
                {
                    foreach (string tenant in connectionString.AdditionallyAllowedTenants)
                    {
                        opts.AdditionallyAllowedTenants.Add(tenant);
                    }
                }

                credential = new WorkloadIdentityCredential(opts);
                break;

            case "environment":
                // Use credentials from the environment
                credential = new EnvironmentCredential();
                break;

            case "azurecli":
                // Use credentials from the Azure CLI
                credential = new AzureCliCredential();
                break;

            case "azurepowershell":
                // Use credentials from the Azure PowerShell modules
                credential = new AzurePowerShellCredential();
                break;

            case "none":
                // Do not use any authentication/authorization (for testing only)
                // This is a no-op
                credential = null;
                break;

            default:
                throw new ArgumentException(
                    $"The connection string contains an unsupported authentication type '{authType}'.",
                    nameof(connectionString));
        }

        DurableTaskSchedulerOptions options = new(endpointAddress, connectionString.TaskHubName, credential);
        return options;
    }
}
