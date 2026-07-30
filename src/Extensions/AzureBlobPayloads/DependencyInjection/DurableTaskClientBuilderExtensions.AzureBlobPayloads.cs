// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core.Interceptors;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Grpc;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Worker.Grpc.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

        UseExternalizedPayloadsCore(builder);

        // Conditional DI: register the auto-purge starter only when the caller opted into auto-purge. Peek the
        // flag now by running the configure delegate against a probe (options configurators are pure setters).
        LargePayloadStorageOptions probe = new();
        configure(probe);
        if (probe.AutoPurge)
        {
            RegisterBlobPurgeJobStarter(builder);
        }

        return builder;
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

    static void RegisterBlobPurgeJobStarter(IDurableTaskClientBuilder builder)
    {
        string builderName = builder.Name;
        builder.Services.AddSingleton<IHostedService>(sp => new BlobPurgeJobStarter(
            sp.GetRequiredService<DurableTaskClient>(),
            sp.GetRequiredService<IOptionsMonitor<LargePayloadStorageOptions>>(),
            builderName,
            sp.GetRequiredService<ILogger<BlobPurgeJobStarter>>()));
    }
}
