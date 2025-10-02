// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask;

/// <summary>
/// DI extensions for configuring a shared Azure Blob payload store used by both client and worker.
/// </summary>
public static class ServiceCollectionExtensionsAzureBlobPayloads
{
    /// <summary>
    /// Registers a shared Azure Blob-based externalized payload store and its options.
    /// The provided options apply to all named Durable Task builders (client/worker),
    /// so <c>UseExternalizedPayloads()</c> can be called without repeating configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The configuration callback for the payload store.</param>
    /// <returns>The original service collection.</returns>
    public static IServiceCollection AddExternalizedPayloadStore(
        this IServiceCollection services,
        Action<LargePayloadStorageOptions> configure)
    {
        Check.NotNull(services);
        Check.NotNull(configure);

        // Apply once to ALL names (IConfigureOptions<T> hits every named options instance),
        // so monitor.Get(builder.Name) in the client/worker extensions will see the same config.
        services.Configure(configure);

        // Provide a single shared IPayloadStore instance built from the default options.
        services.AddSingleton<IPayloadStore>(sp =>
        {
            IOptionsMonitor<LargePayloadStorageOptions> monitor =
                sp.GetRequiredService<IOptionsMonitor<LargePayloadStorageOptions>>();

            LargePayloadStorageOptions opts = monitor.Get(Options.DefaultName);
            return new BlobPayloadStore(opts);
        });

        return services;
    }
}
