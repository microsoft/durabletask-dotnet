// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Net.Client;
using Microsoft.DurableTask.AzureManaged.Internal;
using Microsoft.DurableTask.Client.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Proto = Microsoft.DurableTask.Protobuf.OnDemandSandbox;

namespace Microsoft.DurableTask.Client.AzureManaged;

/// <summary>
/// Extension methods for registering DTS on-demand sandbox activity management clients.
/// </summary>
public static class OnDemandSandboxActivitiesClientServiceCollectionExtensions
{
    /// <summary>
    /// Adds a DTS on-demand sandbox activity management client using the default Durable Task client configuration.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The original service collection, for call chaining.</returns>
    public static IServiceCollection AddDurableTaskSchedulerOnDemandSandboxActivitiesClient(this IServiceCollection services)
        => AddDurableTaskSchedulerOnDemandSandboxActivitiesClient(services, Options.DefaultName);

    /// <summary>
    /// Adds a DTS on-demand sandbox activity management client using a named Durable Task client configuration.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="clientName">The Durable Task client name whose scheduler channel should be reused.</param>
    /// <returns>The original service collection, for call chaining.</returns>
    public static IServiceCollection AddDurableTaskSchedulerOnDemandSandboxActivitiesClient(
        this IServiceCollection services,
        string clientName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clientName);

        services.AddSingleton(provider =>
        {
            DurableTaskSchedulerClientOptions schedulerOptions = provider
                .GetRequiredService<IOptionsMonitor<DurableTaskSchedulerClientOptions>>()
                .Get(clientName);
            GrpcDurableTaskClientOptions options = provider
                .GetRequiredService<IOptionsMonitor<GrpcDurableTaskClientOptions>>()
                .Get(clientName);

            if (options.CallInvoker is { } callInvoker)
            {
                return new OnDemandSandboxActivitiesClient(
                    new OnDemandSandboxActivitiesGrpcTransport(
                        new Proto.OnDemandSandboxActivities.OnDemandSandboxActivitiesClient(callInvoker),
                        attachTaskHubMetadata: false),
                    schedulerOptions.TaskHubName);
            }

            if (options.Channel is GrpcChannel channel)
            {
                return new OnDemandSandboxActivitiesClient(
                    new OnDemandSandboxActivitiesGrpcTransport(
                        new Proto.OnDemandSandboxActivities.OnDemandSandboxActivitiesClient(channel.CreateCallInvoker()),
                        attachTaskHubMetadata: false),
                    schedulerOptions.TaskHubName);
            }

            throw new InvalidOperationException("DTS on-demand sandbox activity management requires a configured Durable Task Scheduler client.");
        });
        return services;
    }
}
