// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Orchestrator input describing the purge job to run.
/// </summary>
/// <param name="JobEntityId">The entity ID of the owning <see cref="BlobPurgeJob"/>.</param>
/// <param name="PurgeBatchSize">The maximum number of tombstoned payloads to request per cycle.</param>
/// <param name="ProcessedCycles">The number of cycles processed since the last continue-as-new.</param>
public sealed record BlobPurgeJobRunRequest(
    EntityInstanceId JobEntityId, int PurgeBatchSize, int ProcessedCycles = 0);

/// <summary>
/// Perpetual orchestrator that drains tombstoned payloads from the backend, deletes their blobs with capped
/// parallelism, and acknowledges the successful deletions so the backend can hard-delete the rows. It idles
/// on a timer when there is nothing to purge and continues-as-new periodically to keep its history small.
/// </summary>
[DurableTask]
public class BlobPurgeJobOrchestrator : TaskOrchestrator<BlobPurgeJobRunRequest, object?>
{
    const int ContinueAsNewFrequency = 5;
    const int MaxParallelDeletes = 32;
    const int DefaultPurgeBatchSize = 500;
    static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(1);

    // Retry policy for the purge activities: 3 attempts with exponential backoff (15s, 30s, capped at 60s).
    static readonly RetryPolicy PurgeActivityRetryPolicy = new(
        maxNumberOfAttempts: 3,
        firstRetryInterval: TimeSpan.FromSeconds(15),
        backoffCoefficient: 2.0,
        maxRetryInterval: TimeSpan.FromSeconds(60));

    /// <inheritdoc/>
    public override async Task<object?> RunAsync(TaskOrchestrationContext context, BlobPurgeJobRunRequest input)
    {
        ILogger logger = context.CreateReplaySafeLogger<BlobPurgeJobOrchestrator>();
        string jobId = input.JobEntityId.Key;

        int batchSize = input.PurgeBatchSize > 0 ? input.PurgeBatchSize : DefaultPurgeBatchSize;
        int processedCycles = input.ProcessedCycles;

        while (true)
        {
            processedCycles++;
            if (processedCycles > ContinueAsNewFrequency)
            {
                context.ContinueAsNew(new BlobPurgeJobRunRequest(input.JobEntityId, batchSize, ProcessedCycles: 0));
                return null!;
            }

            // Stop cleanly if the job has been stopped or removed.
            BlobPurgeJobState? state = await context.Entities.CallEntityAsync<BlobPurgeJobState?>(
                input.JobEntityId, nameof(BlobPurgeJob.Get), null);

            if (state is null || state.Status != BlobPurgeJobStatus.Active)
            {
                logger.BlobPurgeJobOrchestratorStopping(jobId, state?.Status.ToString() ?? "null");
                return null;
            }

            List<TombstonedPayloadDto> tombstones = await context.CallActivityAsync<List<TombstonedPayloadDto>>(
                nameof(GetTombstonedPayloadsActivity),
                batchSize,
                new TaskOptions(PurgeActivityRetryPolicy));

            if (tombstones is null || tombstones.Count == 0)
            {
                // Nothing to purge right now: block on a timer (push-free idle) then check again.
                await context.CreateTimer(IdleDelay, default);
                continue;
            }

            List<PayloadPurgeAckDto> deleted = await this.DeleteBatchAsync(context, tombstones);

            if (deleted.Count > 0)
            {
                await context.CallActivityAsync(
                    nameof(AckPurgedPayloadsActivity),
                    deleted,
                    new TaskOptions(PurgeActivityRetryPolicy));

                await context.Entities.CallEntityAsync(
                    input.JobEntityId, nameof(BlobPurgeJob.RecordPurged), (long)deleted.Count);
            }
        }
    }

    async Task<List<PayloadPurgeAckDto>> DeleteBatchAsync(
        TaskOrchestrationContext context, List<TombstonedPayloadDto> tombstones)
    {
        List<PayloadPurgeAckDto> deleted = new(tombstones.Count);
        List<Task<DeleteOutcome>> tasks = new();

        foreach (TombstonedPayloadDto tombstone in tombstones)
        {
            tasks.Add(this.DeleteOneAsync(context, tombstone));

            if (tasks.Count >= MaxParallelDeletes)
            {
                await DrainAsync(tasks, deleted);
                tasks.Clear();
            }
        }

        if (tasks.Count > 0)
        {
            await DrainAsync(tasks, deleted);
        }

        return deleted;
    }

    static async Task DrainAsync(List<Task<DeleteOutcome>> tasks, List<PayloadPurgeAckDto> deleted)
    {
        DeleteOutcome[] outcomes = await Task.WhenAll(tasks);
        foreach (DeleteOutcome outcome in outcomes)
        {
            // Only acknowledge blobs that were actually deleted; failed tokens stay tombstoned to retry.
            if (outcome.Deleted)
            {
                deleted.Add(outcome.Ack);
            }
        }
    }

    async Task<DeleteOutcome> DeleteOneAsync(TaskOrchestrationContext context, TombstonedPayloadDto tombstone)
    {
        bool deleted = await context.CallActivityAsync<bool>(
            nameof(DeleteExternalBlobActivity),
            tombstone.Token,
            new TaskOptions(PurgeActivityRetryPolicy));

        return new DeleteOutcome(
            deleted,
            new PayloadPurgeAckDto(tombstone.PartitionId, tombstone.InstanceKey, tombstone.PayloadId));
    }

    readonly record struct DeleteOutcome(bool Deleted, PayloadPurgeAckDto Ack);
}
