// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.Entities;
using Microsoft.DurableTask.Client.OrchestrationServiceClientShim;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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
        builder.Services.AddOptions<ShimDurableTaskClientOptions>(builder.Name).Configure(configure);
        builder.Services.TryAddSingleton<IPostConfigureOptions<ShimDurableTaskClientOptions>, OptionsConfigure>();
        builder.Services.TryAddSingleton<IValidateOptions<ShimDurableTaskClientOptions>, OptionsValidator>();
        return builder.UseBuildTarget<ShimDurableTaskClient, ShimDurableTaskClientOptions>();
    }

    static IEntityOrchestrationService? GetEntityService(
        IServiceProvider services, ShimDurableTaskClientOptions options)
    {
        return options.Client as IEntityOrchestrationService
            ?? services.GetService<IEntityOrchestrationService>()
            ?? services.GetService<IOrchestrationServiceClient>() as IEntityOrchestrationService
            ?? services.GetService<IOrchestrationService>() as IEntityOrchestrationService;
    }

    class OptionsConfigure(IServiceProvider services) : IPostConfigureOptions<ShimDurableTaskClientOptions>
    {
        public void PostConfigure(string name, ShimDurableTaskClientOptions options)
        {
            ConfigureClient(services, options);
            ConfigureEntities(name, services, options); // Must be called after ConfigureClient.
        }

        static void ConfigureClient(IServiceProvider services, ShimDurableTaskClientOptions options)
        {
            if (options.Client is not null)
            {
                return;
            }

            // Try to resolve client from service container.
            options.Client = services.GetService<IOrchestrationServiceClient>()
                ?? services.GetService<IOrchestrationService>() as IOrchestrationServiceClient;
        }

        static void ConfigureEntities(string name, IServiceProvider services, ShimDurableTaskClientOptions options)
        {
            if (options.Entities.Queries is null)
            {
                options.Entities.Queries = services.GetService<EntityBackendQueries>()
                    ?? GetEntityService(services, options)?.EntityBackendQueries;
            }

            if (options.Entities.MaxSignalDelayTime is null)
            {
                EntityBackendProperties? properties = services.GetService<IOptionsMonitor<EntityBackendProperties>>()?.Get(name)
                    ?? GetEntityService(services, options)?.EntityBackendProperties;
                options.Entities.MaxSignalDelayTime = properties?.MaximumSignalDelayTime;
            }
        }
    }

    class OptionsValidator : IValidateOptions<ShimDurableTaskClientOptions>
    {
        public ValidateOptionsResult Validate(string name, ShimDurableTaskClientOptions options)
        {
            if (options.Client is null)
            {
                return ValidateOptionsResult.Fail("ShimDurableTaskClientOptions.Client must not be null.");
            }

            if (options.EnableEntitySupport && options.Entities.Queries is null)
            {
                return ValidateOptionsResult.Fail(
                    "ShimDurableTaskClientOptions.Entities.Queries must not be null when entity support is enabled.");
            }

            return ValidateOptionsResult.Success;
        }
    }
}
