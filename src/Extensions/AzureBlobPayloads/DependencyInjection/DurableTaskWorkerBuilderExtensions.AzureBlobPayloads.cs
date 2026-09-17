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
/// Payload storage is configured per host: the <c>PayloadStore</c> is shared by all named builders.
/// If no store is already registered, the first builder calling <c>UseExternalizedPayloads</c> supplies it.
/// Each named worker has its own purge client, which follows that worker's transport and channel lifecycle.
/// A single named worker, or several named workers sharing one storage and backend configuration, is supported.
/// Different storage accounts or different backends in the same host are not supported.
/// </remarks>
public static class DurableTaskWorkerBuilderExtensionsAzureBlobPayloads
{
    static readonly string[] AutoPurgeActivityNames =
    [
        nameof(GetLargePayloadTombstonesActivity),
        nameof(DeleteExternalBlobActivity),
        nameof(ReportLargePayloadPurgeResultsActivity),
    ];

    /// <summary>
    /// Enables externalized payload storage using Azure Blob Storage for the specified worker builder.
    /// </summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="configure">The callback to configure the storage options.</param>
    /// <returns>The original builder, for call chaining.</returns>
    /// <remarks>
    /// Nonempty work item filters include the internal auto-purge orchestrator and activity names, even when
    /// the supplied filters contain only one task category. Null or all-empty filters remain unfiltered.
    /// Internal auto-purge tasks use wildcard version filters and bypass customer worker version checks.
    /// Customer task filters and version policies are unchanged. This does not bypass other worker filters,
    /// registration requirements, or lifecycle constraints, and does not guarantee cross-SDK replay compatibility.
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
    /// Internal auto-purge tasks use wildcard version filters and bypass customer worker version checks.
    /// Customer task filters and version policies are unchanged. This does not bypass other worker filters,
    /// registration requirements, or lifecycle constraints, and does not guarantee cross-SDK replay compatibility.
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
            .PostConfigure<PayloadStore, IOptionsMonitor<LargePayloadStorageOptions>, IServiceProvider>(
                (opt, store, monitor, services) =>
            {
                LargePayloadStorageOptions opts = monitor.Get(builder.Name);

                // Register an interceptor rather than moving Channel onto an intercepted CallInvoker.
                // Clearing Channel would disable the worker's gRPC channel recreation, and requiring a
                // pre-built Channel/CallInvoker would rule out the Address-only configuration.
                opt.Interceptors.Add(new AzureBlobPayloadsSideCarInterceptor(store, opts));

                opt.Capabilities.Add(P.WorkerCapability.LargePayloads);
                opt.ConfigureVersioningExemptions([nameof(BlobPurgeJobOrchestrator)], AutoPurgeActivityNames);

                // Follow the worker's transport instead of capturing one. The worker publishes its effective
                // post-interceptor invoker here at startup and after every channel recreate, which is what
                // keeps the purge activities on the live channel and inside the configured auth chain.
                opt.SetCallInvokerPublisher(services.GetRequiredKeyedService<RebindableCallInvoker>(builder.Name).Rebind);
            });

        // The worker resolves gRPC options before reading its filters. Resolve the fully configured and
        // validated named filters here, after every UseWorkItemFilters PostConfigure, including later calls.
        builder.Services.AddOptions<GrpcDurableTaskWorkerOptions>(builder.Name)
            .PostConfigure<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>(
                (_, filters) => AddAutoPurgeFilters(filters.Get(builder.Name)));

        // Each named worker publishes only to its own invoker. The registry factories below select that
        // worker's client at dispatch, sharing its intercepted transport without opening another connection.
        builder.Services.TryAddKeyedSingleton<RebindableCallInvoker>(builder.Name);
        builder.Services.TryAddKeyedSingleton<LargePayloadPurgeClient>(
            builder.Name, (sp, key) => new(sp.GetRequiredKeyedService<RebindableCallInvoker>(key)));

        // Register the orchestrator/activities that run the singleton auto-purge job. These are ALWAYS
        // registered (never gated on configuration) so that a job a client has turned on always has something
        // to execute here. Workers never call SetLargePayloadAutoPurge - the setting is owned by the explicit
        // client API - but they do fetch and report via the worker's LargePayloadPurgeClient above.
        builder.AddTasks(r =>
        {
            r.AddOrchestrator<BlobPurgeJobOrchestrator>();
            r.AddActivity(nameof(GetLargePayloadTombstonesActivity), sp =>
                ActivatorUtilities.CreateInstance<GetLargePayloadTombstonesActivity>(
                    sp, sp.GetRequiredKeyedService<LargePayloadPurgeClient>(builder.Name)));
            r.AddActivity<DeleteExternalBlobActivity>();
            r.AddActivity(nameof(ReportLargePayloadPurgeResultsActivity), sp =>
                ActivatorUtilities.CreateInstance<ReportLargePayloadPurgeResultsActivity>(
                    sp, sp.GetRequiredKeyedService<LargePayloadPurgeClient>(builder.Name)));
        });

        return builder;
    }

    static void AddAutoPurgeFilters(DurableTaskWorkerWorkItemFilters filters)
    {
        if (filters.Orchestrations.Count == 0 && filters.Activities.Count == 0 && filters.Entities.Count == 0)
        {
            return;
        }

        List<DurableTaskWorkerWorkItemFilters.OrchestrationFilter> orchestrations = filters.Orchestrations
            .Select(filter => string.Equals(filter.Name, nameof(BlobPurgeJobOrchestrator), StringComparison.OrdinalIgnoreCase)
                ? new DurableTaskWorkerWorkItemFilters.OrchestrationFilter(filter.Name, [])
                : filter)
            .ToList();
        if (!orchestrations.Any(filter => string.Equals(
            filter.Name, nameof(BlobPurgeJobOrchestrator), StringComparison.OrdinalIgnoreCase)))
        {
            orchestrations.Add(new(nameof(BlobPurgeJobOrchestrator), []));
        }

        List<DurableTaskWorkerWorkItemFilters.ActivityFilter> activities = filters.Activities
            .Select(filter => AutoPurgeActivityNames.Contains(filter.Name, StringComparer.OrdinalIgnoreCase)
                ? new DurableTaskWorkerWorkItemFilters.ActivityFilter(filter.Name, [])
                : filter)
            .ToList();
        foreach (string name in AutoPurgeActivityNames.Where(name => !activities.Any(filter =>
            string.Equals(filter.Name, name, StringComparison.OrdinalIgnoreCase))))
        {
            activities.Add(new(name, []));
        }

        // Explicit filters may point directly at caller-owned lists, including lists shared with another worker.
        filters.Orchestrations = orchestrations;
        filters.Activities = activities;
    }
}
