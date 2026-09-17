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
    /// <remarks>
    /// Nonempty work item filters include the internal auto-purge orchestrator and activity names, even when
    /// the supplied filters contain only one task category. Null or all-empty filters remain unfiltered.
    /// Existing version filters are preserved; newly added names follow the worker's versioning policy.
    /// Including a name does not override version restrictions or guarantee that the worker is running.
    /// </remarks>
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
    /// <remarks>
    /// Nonempty work item filters include the internal auto-purge orchestrator and activity names, even when
    /// the supplied filters contain only one task category. Null or all-empty filters remain unfiltered.
    /// Existing version filters are preserved; newly added names follow the worker's versioning policy.
    /// Including a name does not override version restrictions or guarantee that the worker is running.
    /// </remarks>
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

        // The worker resolves gRPC options before reading its filters. Resolve the fully configured and
        // validated named filters here, after every UseWorkItemFilters PostConfigure, including later calls.
        builder.Services.AddOptions<GrpcDurableTaskWorkerOptions>(builder.Name)
            .PostConfigure<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>, IOptionsMonitor<DurableTaskWorkerOptions>>(
                (_, filters, options) => AddAutoPurgeFilters(filters.Get(builder.Name), options.Get(builder.Name)));

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

        // Register the orchestrator/activities that run the singleton auto-purge job. These are ALWAYS
        // registered (never gated on configuration) so that a job a client has turned on always has something
        // to execute here. Workers never call SetLargePayloadAutoPurge - the setting is owned by the explicit
        // client API - but they do fetch and report via the worker's LargePayloadPurgeClient above.
        builder.AddTasks(r =>
        {
            r.AddOrchestrator<BlobPurgeJobOrchestrator>();
            r.AddActivity<GetLargePayloadTombstonesActivity>();
            r.AddActivity<DeleteExternalBlobActivity>();
            r.AddActivity<ReportLargePayloadPurgeResultsActivity>();
        });

        return builder;
    }

    static void AddAutoPurgeFilters(DurableTaskWorkerWorkItemFilters filters, DurableTaskWorkerOptions options)
    {
        if (filters.Orchestrations.Count == 0 && filters.Activities.Count == 0 && filters.Entities.Count == 0)
        {
            return;
        }

        // These tasks have unversioned registrations: match automatic filters' wildcard/Strict semantics.
        string[] versions = options.Versioning?.MatchStrategy == DurableTaskWorkerOptions.VersionMatchStrategy.Strict
            ? [options.Versioning.Version ?? string.Empty]
            : [];
        List<DurableTaskWorkerWorkItemFilters.OrchestrationFilter> orchestrations = filters.Orchestrations.ToList();
        if (!orchestrations.Any(filter => string.Equals(
            filter.Name, nameof(BlobPurgeJobOrchestrator), StringComparison.OrdinalIgnoreCase)))
        {
            orchestrations.Add(new(nameof(BlobPurgeJobOrchestrator), versions));
        }

        List<DurableTaskWorkerWorkItemFilters.ActivityFilter> activities = filters.Activities.ToList();
        foreach (string name in new[]
        {
            nameof(GetLargePayloadTombstonesActivity),
            nameof(DeleteExternalBlobActivity),
            nameof(ReportLargePayloadPurgeResultsActivity),
        })
        {
            if (!activities.Any(filter => string.Equals(filter.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                activities.Add(new(name, versions));
            }
        }

        // Explicit filters may point directly at caller-owned lists, including lists shared with another worker.
        filters.Orchestrations = orchestrations;
        filters.Activities = activities;
    }
}
