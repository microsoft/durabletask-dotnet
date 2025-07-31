// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ComponentModel.DataAnnotations;
using Azure.Core;
using Azure.Identity;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.DurableTask.Client;
using GrpcConfig = Grpc.Net.Client.Configuration;

namespace Microsoft.DurableTask;

/// <summary>
/// Options for configuring the Durable Task Scheduler.
/// </summary>
public class DurableTaskSchedulerClientOptions
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
    /// Gets or sets a value indicating whether to allow insecure channel credentials.
    /// This should only be set to true in local development/testing scenarios.
    /// </summary>
    public bool AllowInsecureCredentials { get; set; }

    /// <summary>
    /// Gets or sets the options that determine how and when calls made to the scheduler will be retried.
    /// </summary>
    public ClientRetryOptions? RetryOptions { get; set; }

    /// <summary>
    /// Creates a new instance of <see cref="DurableTaskSchedulerClientOptions"/> from a connection string.
    /// </summary>
    /// <param name="connectionString">The connection string to parse.</param>
    /// <returns>A new instance of <see cref="DurableTaskSchedulerClientOptions"/>.</returns>
    public static DurableTaskSchedulerClientOptions FromConnectionString(string connectionString)
    {
        return FromConnectionString(new DurableTaskSchedulerConnectionString(connectionString));
    }

    /// <summary>
    /// Creates a new instance of <see cref="DurableTaskSchedulerClientOptions"/> from a parsed connection string.
    /// </summary>
    /// <param name="connectionString">The connection string to parse.</param>
    /// <returns>A new instance of <see cref="DurableTaskSchedulerClientOptions"/>.</returns>
    internal static DurableTaskSchedulerClientOptions FromConnectionString(
        DurableTaskSchedulerConnectionString connectionString)
    {
        TokenCredential? credential = GetCredentialFromConnectionString(connectionString);
        return new DurableTaskSchedulerClientOptions()
        {
            EndpointAddress = connectionString.Endpoint,
            TaskHubName = connectionString.TaskHubName,
            Credential = credential,
            AllowInsecureCredentials = credential is null,
        };
    }

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
                metadata.Add("x-user-agent", $"{DurableTaskUserAgentUtil.GetUserAgent(nameof(DurableTaskClient))}");
                if (cache == null)
                {
                    return;
                }

                AccessToken token = await cache.GetTokenAsync(context.CancellationToken);
                metadata.Add("Authorization", $"Bearer {token.Token}");
            });

        GrpcConfig.ServiceConfig? serviceConfig = GrpcRetryPolicyDefaults.DefaultServiceConfig;
        if (this.RetryOptions != null)
        {
            GrpcConfig.RetryPolicy retryPolicy = new GrpcConfig.RetryPolicy
            {
                MaxAttempts = this.RetryOptions.MaxRetries ?? GrpcRetryPolicyDefaults.DefaultMaxAttempts,
                InitialBackoff = TimeSpan.FromMilliseconds(this.RetryOptions.InitialBackoffMs ?? GrpcRetryPolicyDefaults.DefaultInitialBackoffMs),
                MaxBackoff = TimeSpan.FromMilliseconds(this.RetryOptions.MaxBackoffMs ?? GrpcRetryPolicyDefaults.DefaultMaxBackoffMs),
                BackoffMultiplier = this.RetryOptions.BackoffMultiplier ?? GrpcRetryPolicyDefaults.DefaultBackoffMultiplier,
                RetryableStatusCodes = { StatusCode.Unavailable }, // Always retry on Unavailable.
            };

            if (this.RetryOptions.RetryableStatusCodes != null)
            {
                foreach (StatusCode statusCode in this.RetryOptions.RetryableStatusCodes)
                {
                    // Added by default, don't need to have it added twice.
                    if (statusCode == StatusCode.Unavailable)
                    {
                        continue;
                    }

                    retryPolicy.RetryableStatusCodes.Add(statusCode);
                }
            }

            GrpcConfig.MethodConfig methodConfig = new GrpcConfig.MethodConfig
            {
                // MethodName.Default applies this retry policy configuration to all gRPC methods on the channel.
                Names = { GrpcConfig.MethodName.Default },
                RetryPolicy = retryPolicy,
            };

            serviceConfig = new GrpcConfig.ServiceConfig
            {
                MethodConfigs = { methodConfig },
            };
        }

        // Production will use HTTPS, but local testing will use HTTP
        ChannelCredentials channelCreds = endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ?
            ChannelCredentials.SecureSsl :
            ChannelCredentials.Insecure;
        return GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Create(channelCreds, managedBackendCreds),
            UnsafeUseInsecureChannelCallCredentials = this.AllowInsecureCredentials,
            ServiceConfig = serviceConfig,
        });
    }

    static TokenCredential? GetCredentialFromConnectionString(DurableTaskSchedulerConnectionString connectionString)
    {
        string authType = connectionString.Authentication;

        // Parse the supported auth types, in a case-insensitive way and without spaces
        switch (authType.ToLowerInvariant())
        {
            case "defaultazure":
                return new DefaultAzureCredential();
            case "managedidentity":
                return new ManagedIdentityCredential(connectionString.ClientId);
            case "workloadidentity":
                WorkloadIdentityCredentialOptions opts = new WorkloadIdentityCredentialOptions();
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
            case "visualstudio":
                return new VisualStudioCredential();
            case "visualstudiocode":
                return new VisualStudioCodeCredential();
            case "interactivebrowser":
                return new InteractiveBrowserCredential();
            case "none":
                return null;
            default:
                throw new ArgumentException(
                    $"The connection string contains an unsupported authentication type '{authType}'.",
                    nameof(connectionString));
        }
    }

    /// <summary>
    /// Options used to configure retries used when making calls to the Scheduler.
    /// </summary>
    public class ClientRetryOptions
    {
        /// <summary>
        /// Gets or sets the maximum number of times a call should be retried.
        /// </summary>
        public int? MaxRetries { get; set; }

        /// <summary>
        /// Gets or sets the initial backoff in milliseconds.
        /// </summary>
        public int? InitialBackoffMs { get; set; }

        /// <summary>
        /// Gets or sets the maximum backoff in milliseconds.
        /// </summary>
        public int? MaxBackoffMs { get; set; }

        /// <summary>
        /// Gets or sets the backoff multiplier for exponential backoff.
        /// </summary>
        public double? BackoffMultiplier { get; set; }

        /// <summary>
        /// Gets or sets the list of status codes that can be retried.
        /// </summary>
        public IList<StatusCode>? RetryableStatusCodes { get; set; }
    }
}
