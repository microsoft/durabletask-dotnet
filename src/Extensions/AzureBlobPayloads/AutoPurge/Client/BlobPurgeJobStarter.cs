// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Client-side hosted service that ensures the singleton blob payload auto-purge job exists when auto-purge
/// is enabled via <see cref="LargePayloadStorageOptions.AutoPurge"/>. It never blocks host startup: it runs
/// on a background task and retries until the backend is reachable. The job is a whole-scheduler singleton,
/// so racing client processes simply no-op.
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
        if (!opts.AutoPurge)
        {
            this.logger.BlobPurgeDisabled();
            return Task.CompletedTask;
        }

        int batchSize = opts.PayloadPurgeBatchSize > 0 ? opts.PayloadPurgeBatchSize : 500;

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
                if (await this.IsJobActiveAsync(cancellationToken))
                {
                    return;
                }

                BlobPurgeJobOperationRequest request = new(
                    this.entityId,
                    nameof(BlobPurgeJob.Create),
                    new BlobPurgeJobCreationOptions(batchSize));

                await this.client.ScheduleNewOrchestrationInstanceAsync(
                    new TaskName(nameof(ExecuteBlobPurgeJobOperationOrchestrator)),
                    request,
                    cancellationToken);

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

    async Task<bool> IsJobActiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            EntityMetadata<BlobPurgeJobState>? metadata =
                await this.client.Entities.GetEntityAsync<BlobPurgeJobState>(
                    this.entityId, cancellation: cancellationToken);
            return metadata is not null && metadata.State.Status == BlobPurgeJobStatus.Active;
        }
        catch (NotSupportedException)
        {
            // The entity-query API is unavailable on this client; fall back to scheduling the idempotent
            // Create, which no-ops if the job is already active.
            return false;
        }
    }
}
