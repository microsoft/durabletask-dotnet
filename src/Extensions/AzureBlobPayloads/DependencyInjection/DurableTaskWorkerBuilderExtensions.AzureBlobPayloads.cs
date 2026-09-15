// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.DurableTask.Worker.Grpc.Internal;
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
            .PostConfigure<PayloadStore, IOptionsMonitor<LargePayloadStorageOptions>, RebindableCallInvoker>(
                (opt, store, monitor, purgeInvoker) =>
            {
                LargePayloadStorageOptions opts = monitor.Get(builder.Name);

                // Register an interceptor rather than moving Channel onto an intercepted CallInvoker.
                // Clearing Channel would disable the worker's gRPC channel recreation, and requiring a
                // pre-built Channel/CallInvoker would rule out the Address-only configuration.
                opt.Interceptors.Add(new AzureBlobPayloadsSideCarInterceptor(store, opts));

                opt.Capabilities.Add(P.WorkerCapability.LargePayloads);

                // Follow the worker's transport instead of capturing one. The worker publishes its effective
                // post-interceptor invoker here at startup and after every channel recreate, which is what
                // keeps the purge activities on the live channel and inside the configured auth chain.
                opt.SetCallInvokerPublisher(purgeInvoker.Rebind);
            });

        // The auto-purge job is entity-driven: its orchestrator drives the BlobPurgeJob entity, and an
        // orchestrator that touches entities with support off throws (TaskOrchestrationContextWrapper). Enable
        // it whenever externalized payloads are configured - mirroring the client side - so the job can run
        // whenever a client has turned the feature on, whichever host that client lives in.
        builder.Services
            .AddOptions<DurableTaskWorkerOptions>(builder.Name)
            .Configure(options =>
            {
                options.EnableEntitySupport = true;
            });

        // The purge activities talk to the backend over the worker's OWN transport, so a worker-only host
        // (which never registers a DurableTaskClient) can still run the job. The worker's transport is not
        // fixed for the life of the process - it recreates its channel when the current one is wedged, and the
        // invoker it hands out is the one left after the configured interceptors (auth included) have been
        // applied. Capturing options.CallInvoker/Channel here would therefore take a RAW invoker, skip the
        // interceptors, reject the Address-only configuration outright, and keep pointing at channel A after
        // the worker had moved to channel B. Instead, register an indirection the worker publishes into and
        // build the client on that.
        // TryAddSingleton (rather than a keyed/named registration) is deliberate: the consumers -
        // GetLargePayloadTombstonesActivity and ReportLargePayloadPurgeResultsActivity - are constructed from
        // the plain IServiceProvider at dispatch with no worker name in scope, so a keyed registration would
        // have no resolvable consumer. This is the per-host single-configuration constraint documented on the
        // class remarks: in a multi-named-worker host the first builder's options win here. Do not "fix" this
        // into keyed DI - without a worker-name-aware consumer there is nothing to resolve the keyed client.
        builder.Services.TryAddSingleton<RebindableCallInvoker>();
        builder.Services.TryAddSingleton(
            sp => new LargePayloadPurgeClient(sp.GetRequiredService<RebindableCallInvoker>()));

        // Register the entity/orchestrator/activities that run the singleton auto-purge job. These are ALWAYS
        // registered (never gated on configuration) so that a job a client has turned on always has something
        // to execute here. Workers never call SetLargePayloadAutoPurge - the setting is owned by the explicit
        // client API - but they do fetch and report via the worker's LargePayloadPurgeClient above.
        builder.AddTasks(r =>
        {
            r.AddEntity<BlobPurgeJob>();
            r.AddOrchestrator<BlobPurgeJobOrchestrator>();
            r.AddActivity<GetLargePayloadTombstonesActivity>();
            r.AddActivity<DeleteExternalBlobActivity>();
            r.AddActivity<ReportLargePayloadPurgeResultsActivity>();
        });

        return builder;
    }
}
