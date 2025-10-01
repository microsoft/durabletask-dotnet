// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core.Interceptors;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Grpc;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Worker.Grpc.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask;

/// <summary>
/// Extension methods to enable externalized payloads using Azure Blob Storage for Durable Task Client.
/// </summary>
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
        builder.Services.AddSingleton<IPayloadStore>(sp =>
        {
            LargePayloadStorageOptions opts = sp.GetRequiredService<IOptionsMonitor<LargePayloadStorageOptions>>().Get(builder.Name);
            return new BlobPayloadStore(opts);
        });

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
        // Wrap the gRPC CallInvoker with our interceptor when using the gRPC client
        builder.Services
            .AddOptions<GrpcDurableTaskClientOptions>(builder.Name)
            .PostConfigure<IPayloadStore, IOptionsMonitor<LargePayloadStorageOptions>>((opt, store, monitor) =>
            {
                LargePayloadStorageOptions opts = monitor.Get(builder.Name);
                if (opt.Channel is not null)
                {
                    Grpc.Core.CallInvoker invoker = opt.Channel.Intercept(new AzureBlobPayloadsSideCarInterceptor(store, opts));
                    opt.CallInvoker = invoker;

                    // Ensure client uses the intercepted invoker path
                    opt.Channel = null;
                }
                else if (opt.CallInvoker is not null)
                {
                    opt.CallInvoker = opt.CallInvoker.Intercept(new AzureBlobPayloadsSideCarInterceptor(store, opts));
                }
                else
                {
                    throw new ArgumentException(
                        "Channel or CallInvoker must be provided to use Azure Blob Payload Externalization feature");
                }
            });

        return builder;
    }
}
