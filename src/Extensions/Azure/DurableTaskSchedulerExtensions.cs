using Azure.Core;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;

namespace DurableTask.Extensions.Azure;

public static class DurableTaskSchedulerExtensions
{
    // Configure the Durable Task *Worker* to use the Durable Task Scheduler service with the specified options.
    public static void UseDurableTaskScheduler(
        this IDurableTaskWorkerBuilder builder,
        string endpointAddress,
        string taskHubName,
        TokenCredential credential,
        Action<DurableTaskSchedulerOptions>? configure = null)
    {
        DurableTaskSchedulerOptions options = new(endpointAddress, taskHubName, credential);

        configure?.Invoke(options);

        builder.UseGrpc(GetGrpcChannelForOptions(options));
    }

    public static void UseDurableTaskScheduler(
        this IDurableTaskWorkerBuilder builder,
        string connectionString,
        Action<DurableTaskSchedulerOptions>? configure = null)
    {
        var options = DurableTaskSchedulerOptions.FromConnectionString(connectionString);
        configure?.Invoke(options);
        builder.UseGrpc(GetGrpcChannelForOptions(options));
    }

    // Configure the Durable Task *Client* to use the Durable Task Scheduler service with the specified options.
    public static void UseDurableTaskScheduler(
        this IDurableTaskClientBuilder builder,
        string endpointAddress,
        string taskHubName,
        TokenCredential credential,
        Action<DurableTaskSchedulerOptions>? configure = null)
    {
        DurableTaskSchedulerOptions options = new(endpointAddress, taskHubName, credential);

        configure?.Invoke(options);

        builder.UseGrpc(GetGrpcChannelForOptions(options));
    }

    public static void UseDurableTaskScheduler(
        this IDurableTaskClientBuilder builder,
        string connectionString,
        Action<DurableTaskSchedulerOptions>? configure = null)
    {
        var options = DurableTaskSchedulerOptions.FromConnectionString(connectionString);
        configure?.Invoke(options);
        builder.UseGrpc(GetGrpcChannelForOptions(options));
    }

    static GrpcChannel GetGrpcChannelForOptions(DurableTaskSchedulerOptions options)
    {
        if (string.IsNullOrEmpty(options.EndpointAddress))
        {
            throw RequiredOptionMissing(nameof(options.TaskHubName));
        }

        if (string.IsNullOrEmpty(options.TaskHubName))
        {
            throw RequiredOptionMissing(nameof(options.TaskHubName));
        }

        string taskHubName = options.TaskHubName;
        string endpoint = options.EndpointAddress;

        if (!endpoint.Contains("://"))
        {
            endpoint = $"https://{endpoint}";
        }

        string resourceId = options.ResourceId ?? "https://durabletask.io";
        int processId = Environment.ProcessId;
        string workerId = options.WorkerId ?? $"{Environment.MachineName},{processId},{Guid.NewGuid():N}";

        TokenCache? cache =
            options.Credential is not null
                ? new(
                    options.Credential,
                    new(new[] { $"{options.ResourceId}/.default" }),
                    TimeSpan.FromMinutes(5))
                : null;

        CallCredentials managedBackendCreds = CallCredentials.FromInterceptor(
            async (context, metadata) =>
            {
                metadata.Add("taskhub", taskHubName);
                metadata.Add("workerid", workerId);

                if (cache is null)
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
        return GrpcChannel.ForAddress(options.EndpointAddress, new GrpcChannelOptions
            {
                // The same credential is being used for all operations.
                // https://learn.microsoft.com/aspnet/core/grpc/authn-and-authz#set-the-bearer-token-with-callcredentials
                Credentials = ChannelCredentials.Create(channelCreds, managedBackendCreds),

                // TODO: This is not appropriate for use in production settings. Setting this to true should
                //       only be done for local testing. We should hide this setting behind some kind of flag.
                UnsafeUseInsecureChannelCallCredentials = true,
            });
    }

    static Exception RequiredOptionMissing(string optionName)
    {
        return new ArgumentException(message: $"Required option '{optionName}' was not provided.");
    }

    sealed class TokenCache(TokenCredential credential, TokenRequestContext context, TimeSpan margin)
    {
        readonly TokenCredential credential = credential;
        readonly TokenRequestContext context = context;
        readonly TimeSpan margin = margin;

        AccessToken? token;

        public async Task<AccessToken> GetTokenAsync(CancellationToken cancellationToken)
        {
            DateTimeOffset nowWithMargin = DateTimeOffset.UtcNow.Add(this.margin);

            if (this.token is null
                || this.token.Value.RefreshOn < nowWithMargin
                || this.token.Value.ExpiresOn < nowWithMargin)
            {
                this.token = await this.credential.GetTokenAsync(this.context, cancellationToken);
            }

            return this.token.Value;
        }
    }
}