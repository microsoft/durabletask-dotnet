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
/// Client-side hosted service that ensures the singleton blob payload auto-purge job exists. It is registered
/// only when auto-purge is enabled at registration time (see the UseExternalizedPayloads configure overload),
/// so it does not re-check the flag here. It never blocks host startup: it runs on a background task and
/// retries until the backend is reachable. The job is a whole-scheduler singleton, so racing client processes
/// simply no-op.
/// </summary>
sealed class BlobPurgeJobStarter : IHostedService
{
    static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

    readonly DurableTaskClient client;
    readonly IOptionsMonitor<LargePayloadStorageOptions> options;
    readonly string builderName;
    readonly ILogger<BlobPurgeJobStarter> logger;
    readonly EntityInstanceId entityId = new(nameof(BlobPurgeJob), BlobPurgeConstants.JobId);

    CancellationTokenSource? cts;
    Task? ensureTask;

    public BlobPurgeJobStarter(
        DurableTaskClient client,
        IOptionsMonitor<LargePayloadStorageOptions> options,
        string builderName,
        ILogger<BlobPurgeJobStarter> logger)
    {
        this.client = Check.NotNull(client);
        this.options = Check.NotNull(options);
        this.builderName = Check.NotNull(builderName);
        this.logger = Check.NotNull(logger);
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        LargePayloadStorageOptions opts = this.options.Get(this.builderName);
        int batchSize = opts.PayloadPurgeBatchSize > 0 ? opts.PayloadPurgeBatchSize : BlobPurgeConstants.DefaultBatchSize;

        // Do not block host startup; ensure the job on a background task with basic retry until the backend
        // is reachable.
        this.cts = new CancellationTokenSource();
        this.ensureTask = Task.Run(() => this.EnsureJobAsync(batchSize, this.cts.Token), CancellationToken.None);
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

    async Task EnsureJobAsync(int batchSize, CancellationToken cancellationToken)
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
                OrchestrationMetadata? existing = await this.client.GetInstanceAsync(
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

                await this.client.ScheduleNewOrchestrationInstanceAsync(
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
