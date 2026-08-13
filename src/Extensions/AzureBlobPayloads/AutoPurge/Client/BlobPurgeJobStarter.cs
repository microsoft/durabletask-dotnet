// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core.Exceptions;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Client-side hosted service that reconciles the per-task-hub singleton blob payload auto-purge job with the
/// resolved options. It is registered unconditionally by UseExternalizedPayloads and decides what to do at
/// startup, once options are fully resolved: it ensures the job exists when auto-purge is enabled, stops a
/// running job when it is disabled, and no-ops with an error log when the registered store cannot delete. It
/// never blocks host startup - the work runs on a background task that retries until the backend is reachable.
/// The job is a per-task-hub singleton, so racing client processes converge on the same result.
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
    Task? backgroundTask;

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
        // missed every enable path except the inline delegate.
        if (!opts.AutoPurge)
        {
            // Turning auto-purge off has to reach the backend to mean anything. The job is a perpetual
            // orchestrator owned by the task hub, not by this process, so a job created while the flag was on
            // keeps deleting blobs forever no matter how many hosts start with it off. Stop a running one.
            //
            // The store gate below deliberately does NOT apply to this path. Stopping a job requires no ability
            // to delete anything, and a user who has switched to a store that cannot delete is precisely
            // someone who needs the job stopped; that gate's error is worded for the enabled case and would be
            // a false alarm here.
            //
            // The job's state is read before anything is signalled, so an app that never enabled auto-purge
            // signals nothing and no entity is created for it.
            this.cts = new CancellationTokenSource();
            this.backgroundTask = Task.Run(() => this.SignalJobStopAsync(this.cts.Token), CancellationToken.None);
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
        this.backgroundTask = Task.Run(() => this.EnsureJobAsync(client, batchSize, this.cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        this.cts?.Cancel();

        Task? pending = this.backgroundTask;
        if (pending is not null)
        {
            // The background loop observes cancellation and returns promptly; swallow any faulted/cancelled
            // result.
            await Task.WhenAny(pending, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Disposes the cancellation source backing the background task.
    /// </summary>
    /// <remarks>
    /// Deliberately not disposed in <see cref="StopAsync"/>: that method stops waiting as soon as the host's
    /// shutdown token fires, so the background task may still hold this source's token. Disposing it there would
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
                // of this list silently becomes replaceable. Deduping exactly Pending and Running therefore
                // means: while the bridge is alive leave it alone, and in any other state re-run it.
                //
                // Pending is the subtle one and is not optional. Instances are created Pending and only become
                // Running after awaiting their first task, so omitting it would leave a real window in which a
                // just-scheduled bridge is replaced.
                //
                // Re-running a bridge that already finished is safe and close to free: the bridge's only effect
                // is calling Create, which no-ops while the entity is Active, so the cost is one instance
                // replacement plus one entity call per host start.
                //
                // Re-running is also what lets the job self-heal after the entity is removed, for example by
                // CleanEntityStorageAsync. The perpetual orchestrator exits cleanly when it reads back a null
                // entity state, and a removed entity is back to its default Pending status, so the job is left
                // dead with a Completed bridge behind it. Keeping Completed deduped would keep it dead
                // permanently; making it replaceable means the next host start re-runs Create, which finds the
                // entity not Active and rebuilds the job.
                //
                // This also recovers the case where the perpetual orchestrator dies while the entity is still
                // Active. Create leaves the entity's status alone in that case, but it re-signals Run, and the
                // resulting start is resolved by the backend: discarded outright while the orchestrator is
                // alive, and allowed to replace it once it has completed, terminated, failed or been canceled.
                // So a healthy job is left untouched and a dead one is rebuilt, without this side ever having to
                // ask which of the two it is looking at.
                //
                // Recovery is bounded by host starts rather than being continuous: nothing re-signals Run
                // between them, so an orchestrator that dies mid-lifetime stays down until the next Create.
                await client.ScheduleNewOrchestrationInstanceAsync(
                    new TaskName(nameof(ExecuteBlobPurgeJobOperationOrchestrator)),
                    request,
                    new StartOrchestrationOptions(BlobPurgeConstants.StarterInstanceId)
                        .WithDedupeStatuses(
                            OrchestrationRuntimeStatus.Pending,
                            OrchestrationRuntimeStatus.Running),
                    cancellationToken);

                this.logger.BlobPurgeJobEnsured();
                return;
            }
            catch (OrchestrationAlreadyExistsException)
            {
                // Thrown only when the bridge already exists in one of the dedupe statuses above, so under this
                // policy it means another host scheduled the bridge and it is still Pending or Running. That is
                // exactly the concurrent-start race this replaced a status check to close: one create wins and
                // the loser lands here. Either way the singleton is already being set up, so treat it as
                // ensured and stop.
                //
                // Note this is NOT the steady-state path. A bridge that already finished is Completed, which is
                // replaceable, so a later host start re-runs it rather than landing here.
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

    async Task SignalJobStopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Resolved inside the loop rather than on the host-start path. This path is taken by every app
                // that externalizes payloads without auto-purge, and constructing a DurableTaskClient at host
                // start for them is exactly what the enable path's lazy resolution avoids. It also means a
                // client that cannot be resolved yet is retried here instead of throwing out of host startup,
                // which must never happen for a feature the user has turned off.
                DurableTaskClient client = this.clientProvider.GetClient(this.builderName);

                // Held in a local so the query and the signal below are issued against the same object. Both go
                // through this one property, which is also where every "entities are not supported" gate in this
                // SDK lives, so a client that cannot answer the query cannot receive the signal either.
                DurableEntityClient entities = client.Entities;

                // Defaults to true so that every path which fails to establish the job's state falls through to
                // signalling. Signalling is the correctness-critical outcome - a job left running keeps deleting
                // payloads - while skipping it is only an optimization, so the optimization must never be the
                // reason the stop does not happen.
                bool stopNeeded = true;

                try
                {
                    // A query against the instance store, not an entity operation: it never dispatches to the
                    // entity, so it cannot materialize one. That is the whole point of reading through the
                    // client here instead of relying on the entity's own idempotence. Signalling Stop
                    // unconditionally would create the job entity - with default, never-started state - for
                    // every app that externalizes payloads without auto-purge, and would persist a fresh write
                    // on every host start for an app whose job is already stopped.
                    // The three-argument overload is called deliberately. The shorter GetEntityAsync(id,
                    // cancellation) is a virtual forwarder that supplies includeState itself, and binding to a
                    // forwarder rather than to the abstract method is what made an earlier set of tests in this
                    // feature pass against a mock that was never wired up. Naming includeState also states the
                    // requirement directly: this call exists to read the status, so metadata alone is useless.
                    EntityMetadata<BlobPurgeJobState>? metadata = await entities
                        .GetEntityAsync<BlobPurgeJobState>(this.entityId, includeState: true, cancellationToken);

                    // IncludesState is checked rather than reading metadata.State directly: State throws when
                    // the metadata carries none, and an entity whose state has been cleared still reports as
                    // existing until entity storage is cleaned. Such an entity has no running job, so it is
                    // treated the same as an absent one.
                    BlobPurgeJobState? state = metadata is { IncludesState: true } ? metadata.State : null;

                    stopNeeded = state?.Status == BlobPurgeJobStatus.Active;

                    if (!stopNeeded)
                    {
                        this.logger.BlobPurgeJobNotRunning();
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    // Deliberately not rethrown into the retry loop. Retrying would be right for a transient
                    // failure but fatal for a permanent one: a client that never answers this query would keep
                    // the loop spinning and the running job would never be told to stop. Falling through costs
                    // at most the entity write this pre-check exists to avoid.
                    this.logger.BlobPurgeJobStateUnknown(ex);
                }

                if (!stopNeeded)
                {
                    return;
                }

                // Signalled rather than driven through the bridge orchestration: unlike Create, Stop needs no
                // fixed-instance dedupe. It is idempotent in the entity, so concurrent hosts converge on the
                // same state and a repeat costs nothing beyond the signal itself.
                await entities.SignalEntityAsync(
                    this.entityId,
                    nameof(BlobPurgeJob.Stop),
                    null,
                    null,
                    cancellationToken);

                this.logger.BlobPurgeJobStopRequested();
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
