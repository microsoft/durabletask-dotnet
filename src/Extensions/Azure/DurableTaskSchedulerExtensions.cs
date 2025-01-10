﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;

namespace Microsoft.DurableTask.Extensions.Azure;

/// <summary>
/// Extension methods for configuring Durable Task workers and clients to use the Azure Durable Task Scheduler service.
/// </summary>
public static class DurableTaskSchedulerExtensions
{
    /// <summary>
    /// Configures Durable Task worker to use the Azure Durable Task Scheduler service.
    /// </summary>
    /// <param name="builder">The worker builder to configure.</param>
    /// <param name="endpointAddress">The endpoint address of the Durable Task Scheduler service.</param>
    /// <param name="taskHubName">The name of the task hub to connect to.</param>
    /// <param name="credential">The credential to use for authentication.</param>
    /// <param name="configure">Optional callback to configure additional options.</param>
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

    /// <summary>
    /// Configures Durable Task worker to use the Azure Durable Task Scheduler service using a connection string.
    /// </summary>
    /// <param name="builder">The worker builder to configure.</param>
    /// <param name="connectionString">The connection string for the Durable Task Scheduler service.</param>
    /// <param name="configure">Optional callback to configure additional options.</param>
    public static void UseDurableTaskScheduler(
        this IDurableTaskWorkerBuilder builder,
        string connectionString,
        Action<DurableTaskSchedulerOptions>? configure = null)
    {
        var options = DurableTaskSchedulerOptions.FromConnectionString(connectionString);
        configure?.Invoke(options);
        builder.UseGrpc(GetGrpcChannelForOptions(options));
    }

    /// <summary>
    /// Configures Durable Task client to use the Azure Durable Task Scheduler service.
    /// </summary>
    /// <param name="builder">The client builder to configure.</param>
    /// <param name="endpointAddress">The endpoint address of the Durable Task Scheduler service.</param>
    /// <param name="taskHubName">The name of the task hub to connect to.</param>
    /// <param name="credential">The credential to use for authentication.</param>
    /// <param name="configure">Optional callback to configure additional options.</param>
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

    /// <summary>
    /// Configures Durable Task client to use the Azure Durable Task Scheduler service using a connection string.
    /// </summary>
    /// <param name="builder">The client builder to configure.</param>
    /// <param name="connectionString">The connection string for the Durable Task Scheduler service.</param>
    /// <param name="configure">Optional callback to configure additional options.</param>
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
        Check.NotNullOrEmpty(options.EndpointAddress, nameof(options.EndpointAddress));
        Check.NotNullOrEmpty(options.TaskHubName, nameof(options.TaskHubName));

        string taskHubName = options.TaskHubName;
        string endpoint = options.EndpointAddress;

        string resourceId = options.ResourceId ?? "https://durabletask.io";
        int processId = Environment.ProcessId;
        string workerId = options.WorkerId ?? $"{Environment.MachineName},{processId},{Guid.NewGuid():N}";

        TokenCache? cache =
            options.Credential is not null
                ? new(
                    options.Credential,
                    new(new[] { $"{resourceId}/.default" }),
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