// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Azure.Core;
using Azure.Identity;
using Grpc.Core;
using Grpc.Net.Client;

namespace Microsoft.DurableTask;

/// <summary>
/// Options for configuring the Durable Task Scheduler.
/// </summary>
public class DurableTaskSchedulerWorkerOptions
{
    /// <summary>
    /// Gets or sets the endpoint address of the Durable Task Scheduler resource.
    /// Expected to be in the format "https://{scheduler-name}.{region}.durabletask.io".
    /// </summary>
    [Required(ErrorMessage = "Endpoint address is required")]
    public string EndpointAddress { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the task hub resource associated with the Durable Task Scheduler resource.
    /// </summary>
    [Required(ErrorMessage = "Task hub name is required")]
    public string TaskHubName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the credential used to authenticate with the Durable Task Scheduler task hub resource.
    /// </summary>
    public TokenCredential? Credential { get; set; }

    /// <summary>
    /// Gets or sets the resource ID of the Durable Task Scheduler resource.
    /// The default value is https://durabletask.io.
    /// </summary>
    public string ResourceId { get; set; } = "https://durabletask.io";

    /// <summary>
    /// Gets or sets the worker ID used to identify the worker instance.
    /// The default value is a string containing the machine name, process ID, and a unique identifier.
    /// </summary>
    public string WorkerId { get; set; } = $"{Environment.MachineName},{Environment.ProcessId},{Guid.NewGuid():N}";

    /// <summary>
    /// Gets or sets a value indicating whether to allow insecure channel credentials.
    /// This should only be set to true in development/testing scenarios.
    /// </summary>
    public bool AllowInsecureCredentials { get; set; }

    /// <summary>
    /// Creates a new instance of <see cref="DurableTaskSchedulerOptions"/> from a connection string.
    /// </summary>
    /// <param name="connectionString">The connection string to parse.</param>
    /// <returns>A new instance of <see cref="DurableTaskSchedulerOptions"/>.</returns>
    public static DurableTaskSchedulerWorkerOptions FromConnectionString(string connectionString)
    {
        return FromConnectionString(new DurableTaskSchedulerConnectionString(connectionString));
    }

    /// <summary>
    /// Creates a new instance of <see cref="DurableTaskSchedulerOptions"/> from a parsed connection string.
    /// </summary>
    /// <param name="connectionString">The connection string to parse.</param>
    /// <returns>A new instance of <see cref="DurableTaskSchedulerOptions"/>.</returns>
    internal static DurableTaskSchedulerWorkerOptions FromConnectionString(
        DurableTaskSchedulerConnectionString connectionString) => new()
        {
            EndpointAddress = connectionString.Endpoint,
            TaskHubName = connectionString.TaskHubName,
            Credential = GetCredentialFromConnectionString(connectionString),
        };

    /// <summary>
    /// Creates a gRPC channel for communicating with the Durable Task Scheduler service.
    /// </summary>
    /// <returns>A configured <see cref="GrpcChannel"/> instance that can be used to make gRPC calls.</returns>
    public GrpcChannel CreateChannel()
    {
        Verify.NotNull(this.EndpointAddress, nameof(this.EndpointAddress));
        Verify.NotNull(this.TaskHubName, nameof(this.TaskHubName));
        string taskHubName = this.TaskHubName;
        string endpoint = !this.EndpointAddress.Contains("://")
            ? $"https://{this.EndpointAddress}"
            : this.EndpointAddress;
        AccessTokenCache? cache =
            this.Credential is not null
                ? new AccessTokenCache(
                    this.Credential,
                    new TokenRequestContext(new[] { $"{this.ResourceId}/.default" }),
                    TimeSpan.FromMinutes(5))
                : null;
        CallCredentials managedBackendCreds = CallCredentials.FromInterceptor(
            async (context, metadata) =>
            {
                metadata.Add("taskhub", taskHubName);
                metadata.Add("workerid", this.WorkerId);
                if (cache == null)
                {
                    return;
                }

                AccessToken token = await cache.GetTokenAsync(context.CancellationToken);
                metadata.Add("Authorization", $"Bearer {token.Token}");
            });

        // Production will use HTTPS, but local testing will use HTTP
        ChannelCredentials channelCreds = endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ?
            ChannelCredentials.SecureSsl :
            ChannelCredentials.Insecure;
        return GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Create(channelCreds, managedBackendCreds),
            UnsafeUseInsecureChannelCallCredentials = this.AllowInsecureCredentials,
        });
    }

    static TokenCredential? GetCredentialFromConnectionString(DurableTaskSchedulerConnectionString connectionString)
    {
        string authType = connectionString.Authentication;

        // Parse the supported auth types, in a case-insensitive way and without spaces
        switch (authType.ToLower(CultureInfo.InvariantCulture).Replace(" ", string.Empty))
        {
            case "defaultazure":
                return new DefaultAzureCredential();
            case "managedidentity":
                return new ManagedIdentityCredential(connectionString.ClientId);
            case "workloadidentity":
                var opts = new WorkloadIdentityCredentialOptions();
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

                return new WorkloadIdentityCredential(opts);
            case "environment":
                return new EnvironmentCredential();
            case "azurecli":
                return new AzureCliCredential();
            case "azurepowershell":
                return new AzurePowerShellCredential();
            case "none":
                return null;
            default:
                throw new ArgumentException(
                    $"The connection string contains an unsupported authentication type '{authType}'.",
                    nameof(connectionString));
        }
    }
}
