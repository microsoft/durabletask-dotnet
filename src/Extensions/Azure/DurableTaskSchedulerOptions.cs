﻿// Copyright (c) Microsoft Corporation.
﻿// Licensed under the MIT License.

using System.Globalization;
using Azure.Core;
using Azure.Identity;
using Grpc.Core;

namespace Microsoft.DurableTask.Extensions.Azure;

/// <summary>
/// Options for configuring the Durable Task Scheduler.
/// </summary>
public class DurableTaskSchedulerOptions
{
    private readonly string defaultWorkerId;

    /// <summary>
    /// Initializes a new instance of the <see cref="DurableTaskSchedulerOptions"/> class.
    /// </summary>
    internal DurableTaskSchedulerOptions(string endpointAddress, string taskHubName, TokenCredential? credential = null)
    {
        Check.NotNullOrEmpty(endpointAddress, nameof(endpointAddress));
        Check.NotNullOrEmpty(taskHubName, nameof(taskHubName));

        // Add https:// prefix if no protocol is specified
        this.EndpointAddress = !endpointAddress.Contains("://")
            ? $"https://{endpointAddress}"
            : endpointAddress;

        this.TaskHubName = taskHubName;
        this.Credential = credential;

        // Generate the default worker ID once at construction time
        // TODO: More iteration needed over time https://github.com/microsoft/durabletask-dotnet/pull/362#discussion_r1909547102
        this.defaultWorkerId = $"{Environment.MachineName},{Environment.ProcessId},{Guid.NewGuid():N}";
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
    /// The default value is a string containing the machine name, process ID, and a unique identifier.
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

    internal GrpcChannel GetGrpcChannel()
    {
        Check.NotNullOrEmpty(this.EndpointAddress, nameof(this.EndpointAddress));
        Check.NotNullOrEmpty(this.TaskHubName, nameof(this.TaskHubName));

        string taskHubName = this.TaskHubName;
        string endpoint = this.EndpointAddress;

        string resourceId = this.ResourceId ?? "https://durabletask.io";
        string workerId = this.WorkerId ?? this.defaultWorkerId;

        AccessTokenCache? cache =
            this.Credential is not null
                ? new AccessTokenCache(
                    this.Credential,
                    new TokenRequestContext(new[] { $"{resourceId}/.default" }),
                    TimeSpan.FromMinutes(5))
                : null;

        CallCredentials managedBackendCreds = CallCredentials.FromInterceptor(
            async (context, metadata) =>
            {
                metadata.Add("taskhub", taskHubName);
                metadata.Add("workerid", workerId);

                if (cache == null)
                {
                    return;
                }
                
                AccessToken token = await cache.GetTokenAsync(context.CancellationToken);
                metadata.Add("Authorization", $"Bearer {token.Token}");
            });

        // Production will use HTTPS, but local testing will use HTTP
        ChannelCredentials channelCreds = endpoint.StartsWith("https://") ?
            ChannelCredentials.SecureSsl :
            ChannelCredentials.Insecure;
        return GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions
            {
                // The same credential is being used for all operations.
                // https://learn.microsoft.com/aspnet/core/grpc/authn-and-authz#set-the-bearer-token-with-callcredentials
                Credentials = ChannelCredentials.Create(channelCreds, managedBackendCreds),

                // TODO: This is not appropriate for use in production settings. Setting this to true should
                //       only be done for local testing. We should hide this setting behind some kind of flag.
                UnsafeUseInsecureChannelCallCredentials = true,
            });
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