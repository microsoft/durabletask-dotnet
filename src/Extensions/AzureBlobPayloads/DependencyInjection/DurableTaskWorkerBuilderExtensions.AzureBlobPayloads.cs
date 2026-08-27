// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using static Microsoft.DurableTask.Protobuf.LargePayloads.LargePayloadPurge;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask;

/// <summary>
/// Extension methods to enable externalized payloads using Azure Blob Storage for Durable Task Worker.
/// </summary>
/// <remarks>
/// Externalized payloads are configured per host, not per named builder. The <c>PayloadStore</c> and the
/// purge <c>LargePayloadPurgeClient</c> are registered as container-wide singletons, so the first builder
/// in the host that calls <c>UseExternalizedPayloads</c> supplies the configuration that both of them use.
/// Configuring multiple named workers in the same host with different storage accounts or different backends
/// is therefore not supported: later builders silently share the first builder's registration. A single named
/// worker, or several named workers that share one configuration, is fully supported.
/// </remarks>
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

                // Register an interceptor rather than moving Channel onto an intercepted CallInvoker.
                // Clearing Channel would disable the worker's gRPC channel recreation, and requiring a
                // pre-built Channel/CallInvoker would rule out the Address-only configuration.
                opt.Interceptors.Add(new AzureBlobPayloadsSideCarInterceptor(store, opts));

                opt.Capabilities.Add(P.WorkerCapability.LargePayloads);

                // The resolved AutoPurge value is announced to the backend with SetLargePayloadAutoPurge, once
                // per worker connection and before work items are requested, so the backend only tombstones
                // payloads for a task hub whose workers opted in. Null means "no opinion" - the RPC is not sent
                // at all; an explicit true/false is the customer's choice.
                opt.LargePayloadAutoPurgeEnabled = opts.AutoPurge;
            });

        // The auto-purge job is entity-driven: its orchestrator drives the BlobPurgeJob entity, and an
        // orchestrator that touches entities with support off throws (TaskOrchestrationContextWrapper). Enable
        // it whenever externalized payloads are configured - mirroring the client side, and not gated on
        // AutoPurge - so the job can be both driven (enabled path) and stopped (disabled path) either way.
        builder.Services
            .AddOptions<DurableTaskWorkerOptions>(builder.Name)
            .Configure(options =>
            {
                options.EnableEntitySupport = true;
            });

        // The purge activities talk to the backend over the worker's OWN transport, so a worker-only host
        // (which never registers a DurableTaskClient) can still run the job. This resolves the same CallInvoker
        // the worker itself uses, so the RPCs ride its channel and interceptor - no second connection.
        // TryAddSingleton (rather than a keyed/named registration) is deliberate: the consumers -
        // GetLargePayloadTombstonesActivity and ReportLargePayloadPurgeResultsActivity - are constructed from
        // the plain IServiceProvider at dispatch with no worker name in scope, so a keyed registration would
        // have no resolvable consumer. This is the per-host single-configuration constraint documented on the
        // class remarks: in a multi-named-worker host the first builder's options win here. Do not "fix" this
        // into keyed DI - without a worker-name-aware consumer there is nothing to resolve the keyed client.
        builder.Services.TryAddSingleton(sp =>
        {
            GrpcDurableTaskWorkerOptions options =
                sp.GetRequiredService<IOptionsMonitor<GrpcDurableTaskWorkerOptions>>().Get(builder.Name);
            CallInvoker invoker = options.CallInvoker
                ?? options.Channel?.CreateCallInvoker()
                ?? throw new InvalidOperationException(
                    "A gRPC Channel or CallInvoker must be configured on the worker to purge externalized payloads.");
            return new LargePayloadPurgeClient(invoker);
        });

        // Register the entity/orchestrators/activities that run the singleton auto-purge job. These are
        // ALWAYS registered (not gated on AutoPurge) so that a client-enabled job always has something to
        // execute here. The purge activities fetch/report via the worker's LargePayloadPurgeClient above.
        builder.AddTasks(r =>
        {
            r.AddEntity<BlobPurgeJob>();
            r.AddOrchestrator<ExecuteBlobPurgeJobOperationOrchestrator>();
            r.AddOrchestrator<BlobPurgeJobOrchestrator>();
            r.AddActivity<GetLargePayloadTombstonesActivity>();
            r.AddActivity<DeleteExternalBlobActivity>();
            r.AddActivity<ReportLargePayloadPurgeResultsActivity>();
        });

        return builder;
    }
}
