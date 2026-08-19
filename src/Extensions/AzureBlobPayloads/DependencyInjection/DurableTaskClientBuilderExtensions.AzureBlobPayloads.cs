// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Grpc;
using Microsoft.DurableTask.Converters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask;

/// <summary>
/// Extension methods to enable externalized payloads using Azure Blob Storage for Durable Task Client.
/// </summary>
public static class DurableTaskClientBuilderExtensionsAzureBlobPayloads
{
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

        return builder;
    }
}
