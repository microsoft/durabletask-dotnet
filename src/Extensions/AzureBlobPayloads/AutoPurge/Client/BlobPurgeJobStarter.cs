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
sealed class BlobPurgeJobStarter : IHostedService, IDisposable
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

    /// <summary>
    /// Initializes a new instance of the <see cref="BlobPurgeJobStarter"/> class.
    /// </summary>
    /// <param name="clientProvider">The provider used to resolve the named durable task client.</param>
    /// <param name="store">The registered payload store, checked for delete support before starting the job.</param>
    /// <param name="options">The monitor used to read the fully-resolved large payload storage options.</param>
    /// <param name="builderName">The name of the client builder this starter belongs to.</param>
    /// <param name="logger">The logger.</param>
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

    /// <summary>
    /// Disposes the cancellation source backing the background ensure task.
    /// </summary>
    /// <remarks>
    /// Deliberately not disposed in <see cref="StopAsync"/>: that method stops waiting as soon as the host's
    /// shutdown token fires, so the ensure task may still hold this source's token. Disposing it there would
    /// fault that still-running task with an <see cref="ObjectDisposedException"/> when it next registers a
    /// callback. The container disposes singletons after every <see cref="StopAsync"/> has returned, which is
    /// the safe point.
    /// </remarks>
    public void Dispose()
    {
        this.cts?.Dispose();
    }

    async Task EnsureJobAsync(DurableTaskClient client, int batchSize, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                BlobPurgeJobOperationRequest request = new(
                    this.entityId, nameof(BlobPurgeJob.Create), batchSize);

                // The bridge orchestration's only job is to apply the entity's Create once, under a fixed
                // instance id. Whether it should be (re)scheduled is decided by the backend as part of the
                // create call, not by reading its status first: a read-then-schedule pair is two independent
                // RPCs with no atomicity between them, so two hosts starting together can both observe "absent"
                // and both schedule.
                //
                // The dedupe list is an inverted whitelist. The wire policy is computed as
                // (all statuses - dedupe statuses) = the statuses that may be REPLACED, so any status left out
                // of this list silently becomes replaceable. It must therefore name every status that must not
                // be disturbed, not just the interesting ones:
                //   Completed  - re-running a finished bridge is wasteful, and with a fixed id the backend
                //                would purge and replace the terminal instance on every host restart. There is
                //                nothing to gain: the bridge would only re-signal Create, which no-ops while
                //                the entity is Active.
                //   Canceled   - terminal and not a failed setup, so it is left alone like Completed.
                //   Pending    - instances are created Pending and only become Running after awaiting their
                //                first task, so omitting it would leave a real window in which a just-scheduled
                //                bridge is replaced.
                //   Running,
                //   Suspended  - still alive; the job is already set up.
                // Failed and Terminated are deliberately absent, which makes them the only replaceable
                // statuses: those may never have applied Create, so rescheduling is what lets a failed setup
                // self-heal. Terminated must also stay out of the list because supplying it alongside a
                // reusable running status is rejected outright.
#pragma warning disable CS0618 // Canceled is obsolete, but is a real dedupe status and must be named here.
                await client.ScheduleNewOrchestrationInstanceAsync(
                    new TaskName(nameof(ExecuteBlobPurgeJobOperationOrchestrator)),
                    request,
                    new StartOrchestrationOptions(BlobPurgeConstants.StarterInstanceId)
                        .WithDedupeStatuses(
                            OrchestrationRuntimeStatus.Completed,
                            OrchestrationRuntimeStatus.Canceled,
                            OrchestrationRuntimeStatus.Pending,
                            OrchestrationRuntimeStatus.Running,
                            OrchestrationRuntimeStatus.Suspended),
                    cancellationToken);
#pragma warning restore CS0618

                this.logger.BlobPurgeJobEnsured();
                return;
            }
            catch (OrchestrationAlreadyExistsException)
            {
                // The expected steady-state outcome, not a rare race: the backend throws this whenever the
                // bridge already exists in one of the dedupe statuses above, which is every host start after
                // the first one succeeded. It means the singleton is already set up, so treat it as ensured
                // and stop. It also covers the race this replaced a status check to close - two hosts starting
                // together - because exactly one create wins and the loser lands here.
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
