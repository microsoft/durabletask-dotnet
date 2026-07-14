// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core.Interceptors;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using P = Microsoft.DurableTask.Protobuf;

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
        // Reuse the shared payload store when one is already registered (e.g. via AddExternalizedPayloadStore or
        // the client builder in the same process); only register our own as a fallback so we never create a
        // second, redundant PayloadStore.
        builder.Services.TryAddSingleton<PayloadStore>(sp =>
        {
            LargePayloadStorageOptions opts = sp.GetRequiredService<IOptionsMonitor<LargePayloadStorageOptions>>().Get(builder.Name);
            return new BlobPayloadStore(opts);
        });

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

                opt.Capabilities.Add(P.WorkerCapability.LargePayloads);
            });

        // Register the entity/orchestrators/activities that run the singleton auto-purge job. These are
        // ALWAYS registered (not gated on AutoPurge) so that a client-enabled job always has something to
        // execute here. The purge activities fetch/ack via the injected DurableTaskClient.
        builder.AddTasks(r =>
        {
            r.AddEntity<BlobPurgeJob>();
            r.AddOrchestrator<ExecuteBlobPurgeJobOperationOrchestrator>();
            r.AddOrchestrator<BlobPurgeJobOrchestrator>();
            r.AddActivity<GetTombstonedPayloadsActivity>();
            r.AddActivity<DeleteExternalBlobActivity>();
            r.AddActivity<AckPurgedPayloadsActivity>();
        });

        return builder;
    }
}
