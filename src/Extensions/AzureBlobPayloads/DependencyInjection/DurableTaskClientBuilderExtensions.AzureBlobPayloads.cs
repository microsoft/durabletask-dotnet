// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Grpc;
using Microsoft.DurableTask.Converters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask;

/// <summary>
/// Extension methods to enable externalized payloads using Azure Blob Storage for Durable Task Client.
/// </summary>
/// <remarks>
/// Externalized payloads are configured per host, not per named builder. The <c>PayloadStore</c> is registered
/// as a container-wide singleton (shared with the worker builder in the same host), so the first builder that
/// calls <c>UseExternalizedPayloads</c> supplies the configuration the whole host uses. Configuring multiple
/// named clients in the same host with different storage accounts or different backends is therefore not
/// supported: later builders silently share the first builder's registration. A single named client, or
/// several named clients that share one configuration, is fully supported.
/// </remarks>
public static class DurableTaskClientBuilderExtensionsAzureBlobPayloads
{
    /// <summary>
    /// Enables externalized payload storage using Azure Blob Storage for the specified client builder.
    /// </summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="configure">The callback to configure the storage options.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskClientBuilder UseExternalizedPayloads(
        this IDurableTaskClientBuilder builder,
        Action<LargePayloadStorageOptions> configure)
    {
        Check.NotNull(builder);
        Check.NotNull(configure);

        builder.Services.Configure(builder.Name, configure);

        return UseExternalizedPayloadsCore(builder);
    }

    /// <summary>
    /// Enables externalized payload storage using a pre-configured shared payload store.
    /// This overload helps ensure client and worker use the same configuration.
    /// </summary>
    /// <param name="builder">The builder to configure.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskClientBuilder UseExternalizedPayloads(
        this IDurableTaskClientBuilder builder)
    {
        Check.NotNull(builder);
        return UseExternalizedPayloadsCore(builder);
    }

    static IDurableTaskClientBuilder UseExternalizedPayloadsCore(IDurableTaskClientBuilder builder)
    {
        // Reuse the shared payload store when one is already registered (e.g. via AddExternalizedPayloadStore or
        // the worker builder in the same process); only register our own as a fallback so we never create a
        // second, redundant PayloadStore.
        builder.Services.TryAddSingleton<PayloadStore>(sp =>
        {
            LargePayloadStorageOptions opts = sp.GetRequiredService<IOptionsMonitor<LargePayloadStorageOptions>>().Get(builder.Name);
            return new BlobPayloadStore(opts);
        });

        // Wrap the gRPC CallInvoker with our interceptor when using the gRPC client
        builder.Services
            .AddOptions<GrpcDurableTaskClientOptions>(builder.Name)
            .PostConfigure<PayloadStore, IOptionsMonitor<LargePayloadStorageOptions>>((opt, store, monitor) =>
            {
                LargePayloadStorageOptions opts = monitor.Get(builder.Name);

                // Register an interceptor rather than moving Channel onto an intercepted CallInvoker.
                // Clearing Channel would disable the client's gRPC channel recreation, and requiring a
                // pre-built Channel/CallInvoker would rule out the Address-only configuration.
                opt.Interceptors.Add(new AzureBlobPayloadsSideCarInterceptor(store, opts));
            });

        // The explicit auto-purge API (SetLargePayloadAutoPurgeAsync) reaches the singleton job through
        // client.Entities on BOTH paths - enabling signals Create, disabling signals Stop - so entity support
        // must be on whenever externalized payloads are configured. Set it on the base options so an explicit
        // UseGrpc client that disables entity support still wins (DurableTaskClientOptions.ApplyTo copies this
        // value only when the derived options did not set it explicitly).
        builder.Configure(options =>
        {
            options.EnableEntitySupport = true;
        });

        return builder;
    }
}
