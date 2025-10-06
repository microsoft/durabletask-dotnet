// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask;

/// <summary>
/// Extension methods to enable externalized payloads using Azure Blob Storage for Durable Task Worker.
/// </summary>
public static class DurableTaskWorkerBuilderExtensionsAzureBlobPayloads
{
    /// <summary>
    /// Enables externalized payload storage using Azure Blob Storage for the specified worker builder.
    /// </summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="configure">The callback to configure the storage options.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UseExternalizedPayloads(
        this IDurableTaskWorkerBuilder builder,
        Action<LargePayloadStorageOptions> configure)
    {
        Check.NotNull(builder);
        Check.NotNull(configure);

        builder.Services.Configure(builder.Name, configure);
        builder.Services.AddSingleton<PayloadStore>(sp =>
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
    public static IDurableTaskWorkerBuilder UseExternalizedPayloads(
        this IDurableTaskWorkerBuilder builder)
    {
        Check.NotNull(builder);
        return UseExternalizedPayloadsCore(builder);
    }

    static IDurableTaskWorkerBuilder UseExternalizedPayloadsCore(IDurableTaskWorkerBuilder builder)
    {
        // Wrap the gRPC CallInvoker with our interceptor when using the gRPC worker
        builder.Services
            .AddOptions<GrpcDurableTaskWorkerOptions>(builder.Name)
            .PostConfigure<PayloadStore, IOptionsMonitor<LargePayloadStorageOptions>>((opt, store, monitor) =>
            {
                LargePayloadStorageOptions opts = monitor.Get(builder.Name);
                if (opt.Channel is not null)
                {
                    var invoker = opt.Channel.Intercept(new AzureBlobPayloadsSideCarInterceptor(store, opts));
                    opt.CallInvoker = invoker;

                    // Ensure worker uses the intercepted invoker path
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
