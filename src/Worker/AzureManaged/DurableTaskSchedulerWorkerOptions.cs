// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Azure.Core;
using Azure.Identity;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Worker.Hosting;

namespace Microsoft.DurableTask;

/// <summary>
/// Options for configuring the Durable Task Scheduler.
/// </summary>
public class DurableTaskSchedulerWorkerOptions
{
    readonly string defaultResourceId = DurableTaskSchedulerResourceId.GetDefault();
    string? resourceId;

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
    /// <remarks>Configure the authority host on this credential, separately from <see cref="ResourceId"/>.</remarks>
    public TokenCredential? Credential { get; set; }

    /// <summary>
    /// Gets or sets the token audience URI, not an Azure Resource Manager resource path.
    /// </summary>
    /// <remarks>
    /// Null or empty values use the default resolved when these options are created:
    /// <c>https://durabletask.azure.us</c> when <c>REGION_NAME</c> starts with <c>usgov</c> or
    /// <c>usdod</c> (case-insensitive), or <c>https://durabletask.io</c> otherwise.
    /// Explicit values have surrounding whitespace, trailing slashes, and one existing
    /// <c>/.default</c> suffix removed. Token requests append <c>/.default</c> to the result.
    /// This does not change <see cref="EndpointAddress"/> or the credential's authority host.
    /// </remarks>
    /// <exception cref="ArgumentException">The explicit value is empty after normalization.</exception>
    [AllowNull]
    public string ResourceId
    {
        get => this.resourceId ?? this.defaultResourceId;
        set => this.resourceId = DurableTaskSchedulerResourceId.Normalize(value);
    }

    /// <summary>
    /// Gets or sets the worker ID used to identify the worker instance.
    /// The default value is a string containing the machine name, process ID, and a unique identifier.
    /// </summary>
    public string WorkerId { get; set; } = $"{Environment.MachineName},{Environment.ProcessId},{Guid.NewGuid():N}";

    /// <summary>
    /// Gets or sets a value indicating whether to allow insecure channel credentials.
    /// This should only be set to true in local development/testing scenarios.
    /// </summary>
    public bool AllowInsecureCredentials { get; set; }

    /// <summary>
    /// Creates a new instance of <see cref="DurableTaskSchedulerWorkerOptions"/> from a connection string.
    /// </summary>
    /// <param name="connectionString">The connection string to parse.</param>
    /// <remarks>
    /// Supports an optional <c>ResourceId</c> token audience and an independent <c>AuthorityHost</c>
    /// HTTPS URI for Azure Identity credentials that support authority configuration. Omitting
    /// <c>AuthorityHost</c> preserves Azure Identity defaults, including <c>AZURE_AUTHORITY_HOST</c>.
    /// Managed identity uses its hosting environment; developer tools may need separate cloud configuration.
    /// </remarks>
    /// <returns>A new instance of <see cref="DurableTaskSchedulerWorkerOptions"/>.</returns>
    public static DurableTaskSchedulerWorkerOptions FromConnectionString(string connectionString)
    {
        return FromConnectionString(new DurableTaskSchedulerConnectionString(connectionString));
    }

    /// <summary>
    /// Creates a new instance of <see cref="DurableTaskSchedulerWorkerOptions"/> from a parsed connection string.
    /// </summary>
    /// <param name="connectionString">The connection string to parse.</param>
    /// <returns>A new instance of <see cref="DurableTaskSchedulerWorkerOptions"/>.</returns>
    internal static DurableTaskSchedulerWorkerOptions FromConnectionString(
        DurableTaskSchedulerConnectionString connectionString)
    {
        TokenCredential? credential = GetCredentialFromConnectionString(connectionString);
        return new DurableTaskSchedulerWorkerOptions()
        {
            EndpointAddress = connectionString.Endpoint,
            TaskHubName = connectionString.TaskHubName,
            Credential = credential,
            ResourceId = connectionString.ResourceId,
            AllowInsecureCredentials = credential is null,
        };
    }

    /// <summary>
    /// Copies an already normalized audience without stripping a second meaningful <c>/.default</c> segment.
    /// </summary>
    /// <param name="source">The options with the resolved audience.</param>
    internal void CopyResourceIdFrom(DurableTaskSchedulerWorkerOptions source) => this.resourceId = source.ResourceId;

    /// <summary>
    /// Creates a gRPC channel for communicating with the Durable Task Scheduler service.
    /// </summary>
    /// <returns>A configured <see cref="GrpcChannel"/> instance that can be used to make gRPC calls.</returns>
    internal GrpcChannel CreateChannel()
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
                // Add user agent header with durabletask-dotnet and DLL version from util
                metadata.Add("x-user-agent", $"{DurableTaskUserAgentUtil.GetUserAgent(nameof(DurableTaskWorker))}");
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
            ServiceConfig = GrpcRetryPolicyDefaults.DefaultServiceConfig,
        });
    }

    static TokenCredential? GetCredentialFromConnectionString(DurableTaskSchedulerConnectionString connectionString)
    {
        string authType = connectionString.Authentication;

        // Parse the supported auth types, in a case-insensitive way and without spaces
        switch (authType.ToLowerInvariant())
        {
            case "defaultazure":
                return new DefaultAzureCredential(connectionString.CreateCredentialOptions<DefaultAzureCredentialOptions>()); // CodeQL [SM05137] Use DefaultAzureCredential explicitly for local development and is decided by the user
            case "managedidentity":
                return new ManagedIdentityCredential(connectionString.ClientId);
            case "workloadidentity":
                WorkloadIdentityCredentialOptions opts = connectionString.CreateCredentialOptions<WorkloadIdentityCredentialOptions>();
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
                return new EnvironmentCredential(connectionString.CreateCredentialOptions<EnvironmentCredentialOptions>());
            case "azurecli":
                return new AzureCliCredential();
            case "azurepowershell":
                return new AzurePowerShellCredential();
            case "visualstudio":
                return new VisualStudioCredential(connectionString.CreateCredentialOptions<VisualStudioCredentialOptions>());
            case "visualstudiocode":
                return new VisualStudioCodeCredential(connectionString.CreateCredentialOptions<VisualStudioCodeCredentialOptions>());
            case "interactivebrowser":
                return new InteractiveBrowserCredential(connectionString.CreateCredentialOptions<InteractiveBrowserCredentialOptions>());
            case "none":
                return null;
            default:
                throw new ArgumentException(
                    $"The connection string contains an unsupported authentication type '{authType}'.",
                    nameof(connectionString));
        }
    }
}
