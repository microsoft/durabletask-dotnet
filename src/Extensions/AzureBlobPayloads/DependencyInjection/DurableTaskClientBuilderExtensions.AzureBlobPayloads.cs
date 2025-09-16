// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Converters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Extension methods to enable externalized payloads using Azure Blob Storage for Durable Task Client.
/// </summary>
public static class DurableTaskClientBuilderExtensionsAzureBlobPayloads
{
    /// <summary>
    /// Enables externalized payload storage using Azure Blob Storage for the specified client builder.
    /// </summary>
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

        // Wrap the gRPC CallInvoker with our interceptor when using the gRPC client
        builder.Services
            .AddOptions<GrpcDurableTaskClientOptions>(builder.Name)
            .PostConfigure<IPayloadStore, IOptionsMonitor<LargePayloadStorageOptions>>((opt, store, monitor) =>
            {
                LargePayloadStorageOptions opts = monitor.Get(builder.Name);
                if (opt.Channel is not null)
                {
                    var invoker = opt.Channel.Intercept(new Worker.Grpc.Internal.AzureBlobPayloadsInterceptor(store, opts));
                    opt.CallInvoker = invoker;
                }
                else if (opt.CallInvoker is not null)
                {
                    opt.CallInvoker = opt.CallInvoker.Intercept(new Worker.Grpc.Internal.AzureBlobPayloadsInterceptor(store, opts));
                }
                else if (!string.IsNullOrEmpty(opt.Address))
                {
                    // Channel will be built later; we can't intercept here. This will be handled in the client if CallInvoker is null.
                }
            });

        // builder.Services
        //     .AddOptions<DurableTaskClientOptions>(builder.Name)
        //     .PostConfigure<IPayloadStore, IOptionsMonitor<LargePayloadStorageOptions>>((opt, store, monitor) =>
        //     {
        //         LargePayloadStorageOptions opts = monitor.Get(builder.Name);
        //         DataConverter inner = opt.DataConverter ?? Converters.JsonDataConverter.Default;
        //         opt.DataConverter = new LargePayloadDataConverter(inner, store, opts);
        //     });

        return builder;
    }
}


