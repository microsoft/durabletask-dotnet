// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Net.Client;
using Microsoft.DurableTask.Client.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Proto = Microsoft.DurableTask.Protobuf.Serverless;

namespace Microsoft.DurableTask.Client.AzureManaged;

/// <summary>
/// Extension methods for registering DTS serverless activity management clients.
/// </summary>
public static class ServerlessActivitiesClientServiceCollectionExtensions
{
    /// <summary>
    /// Adds a DTS serverless activity management client using the default Durable Task client configuration.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The original service collection, for call chaining.</returns>
    public static IServiceCollection AddDurableTaskSchedulerServerlessActivitiesClient(this IServiceCollection services)
        => AddDurableTaskSchedulerServerlessActivitiesClient(services, Options.DefaultName);

    /// <summary>
    /// Adds a DTS serverless activity management client using a named Durable Task client configuration.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="clientName">The Durable Task client name whose scheduler channel should be reused.</param>
    /// <returns>The original service collection, for call chaining.</returns>
    public static IServiceCollection AddDurableTaskSchedulerServerlessActivitiesClient(
        this IServiceCollection services,
        string clientName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clientName);

        services.AddSingleton(provider =>
        {
            GrpcDurableTaskClientOptions options = provider
                .GetRequiredService<IOptionsMonitor<GrpcDurableTaskClientOptions>>()
                .Get(clientName);

            if (options.CallInvoker is { } callInvoker)
            {
                return new ServerlessActivitiesClient(new Proto.ServerlessActivities.ServerlessActivitiesClient(callInvoker));
            }

            if (options.Channel is GrpcChannel channel)
            {
                return new ServerlessActivitiesClient(new Proto.ServerlessActivities.ServerlessActivitiesClient(channel.CreateCallInvoker()));
            }

            throw new InvalidOperationException("DTS serverless activity management requires a configured Durable Task Scheduler client.");
        });
        return services;
    }
}
