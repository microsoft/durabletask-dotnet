// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Microsoft.DurableTask.Client.OrchestrationServiceClientShim;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Extension methods for adding Durable Task support to .NET hosted services, such as ASP.NET Core hosts.
/// </summary>
public static class DurableTaskClientBuilderExtensions
{
    /// <summary>Configures the <see cref="IDurableTaskClientBuilder" /> to be a client backed by a
    /// <see cref="IOrchestrationServiceClient" />.
    /// </summary>
    /// <remarks>
    /// This must be called independently of worker registration.
    /// </remarks>
    /// <param name="builder">The builder to configure.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskClientBuilder UseOrchestrationService(this IDurableTaskClientBuilder builder)
        => builder.UseOrchestrationService(opt => { });

    /// <summary>
    /// Configures the <see cref="IDurableTaskClientBuilder" /> to be a client backed by a
    /// <see cref="IOrchestrationServiceClient" />.
    /// </summary>
    /// <remarks>
    /// This must be called independently of worker registration.
    /// </remarks>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="client">The orchestration service client to use.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskClientBuilder UseOrchestrationService(
        this IDurableTaskClientBuilder builder, IOrchestrationServiceClient client)
        => builder.UseOrchestrationService(opt =>
        {
            opt.Client = client;
        });

    /// <summary>Configures the <see cref="IDurableTaskClientBuilder" /> to be a client backed by a
    /// <see cref="IOrchestrationServiceClient" />.
    /// </summary>
    /// <remarks>
    /// This must be called independently of worker registration.
    /// </remarks>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="configure">The action to configure the client options.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskClientBuilder UseOrchestrationService(
        this IDurableTaskClientBuilder builder, Action<ShimDurableTaskClientOptions> configure)
    {
        Check.NotNull(builder);
        Check.NotNull(configure);
        builder.Services.Configure(builder.Name, configure);
        builder.Services.AddOptions<ShimDurableTaskClientOptions>(builder.Name)
            .PostConfigure<IServiceProvider>((opt, sp) =>
            {
                if (opt.Client is not null)
                {
                    return;
                }

                // Try to resolve client from service container.
                opt.Client = sp.GetService<IOrchestrationServiceClient>()
                    ?? sp.GetService<IOrchestrationService>() as IOrchestrationServiceClient;
            })
            .Validate(x => x.Client is not null, "ShimDurableTaskClientOptions.Client must not be null.");

        return builder.UseBuildTarget<ShimDurableTaskClient, ShimDurableTaskClientOptions>();
    }
}
