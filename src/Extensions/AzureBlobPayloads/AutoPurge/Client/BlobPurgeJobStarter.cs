// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core.Exceptions;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Client-side hosted service that ensures the per-task-hub singleton blob payload auto-purge job exists. It is
/// registered unconditionally by UseExternalizedPayloads and decides what to do at startup, once options are
/// fully resolved: it no-ops silently when auto-purge is disabled, and no-ops with an error log when the
/// registered store cannot delete. It never blocks host startup - the ensure work runs on a background task
/// that retries until the backend is reachable. The job is a per-task-hub singleton, so racing client
/// processes simply no-op.
/// </summary>
sealed class BlobPurgeJobStarter : IHostedService
{
    static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

    readonly IDurableTaskClientProvider clientProvider;
    readonly PayloadStore store;
    readonly IOptionsMonitor<LargePayloadStorageOptions> options;
    readonly string builderName;
    readonly ILogger<BlobPurgeJobStarter> logger;
    readonly EntityInstanceId entityId = new(nameof(BlobPurgeJob), BlobPurgeConstants.JobId);

    CancellationTokenSource? cts;
    Task? ensureTask;

    public BlobPurgeJobStarter(
        IDurableTaskClientProvider clientProvider,
        PayloadStore store,
        IOptionsMonitor<LargePayloadStorageOptions> options,
        string builderName,
        ILogger<BlobPurgeJobStarter> logger)
    {
        this.clientProvider = Check.NotNull(clientProvider);
        this.store = Check.NotNull(store);
        this.options = Check.NotNull(options);
        this.builderName = Check.NotNull(builderName);
        this.logger = Check.NotNull(logger);
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        LargePayloadStorageOptions opts = this.options.Get(this.builderName);

        // Not opted in. The starter is registered unconditionally by UseExternalizedPayloads because whether
        // auto-purge is enabled can only be known once options are fully resolved - the flag can be set by the
        // inline configure delegate, services.Configure, configuration binding or PostConfigure. Deciding at
        // registration time (by running the delegate against a probe instance) both invoked user code twice and
        // missed every enable path except the inline delegate. This is the normal path for apps that externalize
        // payloads without auto-purge, so it returns silently without logging.
        if (!opts.AutoPurge)
        {
            return Task.CompletedTask;
        }

        // Auto-purge deletes blobs through the store, but PayloadStore.DeleteAsync is virtual and its base
        // implementation throws NotSupportedException. A store that cannot delete would fail every single
        // payload, so refuse to start the job rather than spin against the backend - and rather than ack rows
        // whose blobs were never deleted, which would destroy the backend's record of what still needs cleanup.
        // This is a configuration error and is surfaced at startup, where it is cheapest to notice.
        if (this.store is not BlobPayloadStore)
        {
            this.logger.BlobPurgeStoreCannotDelete(this.store.GetType().FullName);
            return Task.CompletedTask;
        }

        // Resolve the client by builder name rather than by type: a named client builder must get its own
        // client, and resolving lazily here - after the AutoPurge gate - avoids constructing a DurableTaskClient
        // at host start for apps that externalize payloads without auto-purge.
        DurableTaskClient client = this.clientProvider.GetClient(this.builderName);

        int batchSize = opts.PayloadPurgeBatchSize;

        // Do not block host startup; ensure the job on a background task with basic retry until the backend
        // is reachable.
        this.cts = new CancellationTokenSource();
        this.ensureTask = Task.Run(() => this.EnsureJobAsync(client, batchSize, this.cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        this.cts?.Cancel();

        Task? pending = this.ensureTask;
        if (pending is not null)
        {
            // The ensure loop observes cancellation and returns promptly; swallow any faulted/cancelled result.
            await Task.WhenAny(pending, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
        }
    }

    async Task EnsureJobAsync(DurableTaskClient client, int batchSize, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // The singleton is already guaranteed by the entity's fixed key (Create no-ops when the job is
                // active) and the orchestrator's fixed instance id. The bridge orchestration's only job is to
                // apply the entity's Create once, under a fixed instance id. Before (re)scheduling it, check the
                // existing bridge: if it already Completed - or is still alive (Running/Pending/Suspended) - the
                // job is set up, so do not reschedule. (Re-running a Completed bridge is wasteful: with a fixed
                // id and no dedupe policy the backend would purge and replace the terminal instance on every
                // host restart.) Only (re)schedule when the bridge is absent, or ended in a Failed/Terminated
                // state that may never have applied Create - which lets a failed setup self-heal.
                OrchestrationMetadata? existing = await client.GetInstanceAsync(
                    BlobPurgeConstants.StarterInstanceId, cancellationToken);

                bool needsSchedule = existing is null
                    or { RuntimeStatus: OrchestrationRuntimeStatus.Failed or OrchestrationRuntimeStatus.Terminated };
                if (!needsSchedule)
                {
                    this.logger.BlobPurgeJobEnsured();
                    return;
                }

                BlobPurgeJobOperationRequest request = new(
                    this.entityId, nameof(BlobPurgeJob.Create), batchSize);

                await client.ScheduleNewOrchestrationInstanceAsync(
                    new TaskName(nameof(ExecuteBlobPurgeJobOperationOrchestrator)),
                    request,
                    new StartOrchestrationOptions(BlobPurgeConstants.StarterInstanceId),
                    cancellationToken);

                this.logger.BlobPurgeJobEnsured();
                return;
            }
            catch (OrchestrationAlreadyExistsException)
            {
                // Race: another client scheduled the bridge between our status check and schedule call. That is
                // fine - the singleton is already kicked off; treat it as ensured and stop.
                this.logger.BlobPurgeJobEnsured();
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                this.logger.BlobPurgeStarterRetry(ex);
                try
                {
                    await Task.Delay(RetryDelay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
