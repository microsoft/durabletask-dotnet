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
    static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(1);
    static readonly TimeSpan ErrorBackoff = TimeSpan.FromMinutes(1);

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

        int batchSize = input.PurgeBatchSize > 0 ? input.PurgeBatchSize : BlobPurgeConstants.DefaultBatchSize;
        int processedCycles = input.ProcessedCycles;

        while (true)
        {
            processedCycles++;
            if (processedCycles > ContinueAsNewFrequency)
            {
                context.ContinueAsNew(new BlobPurgeJobRunRequest(input.JobEntityId, batchSize, ProcessedCycles: 0));
                return null!;
            }

            try
            {
                // Stop cleanly if the job has been stopped or removed.
                BlobPurgeJobState? state = await context.Entities.CallEntityAsync<BlobPurgeJobState?>(
                    input.JobEntityId, nameof(BlobPurgeJob.Get), null);

                if (state is null || state.Status != BlobPurgeJobStatus.Active)
                {
                    logger.BlobPurgeJobOrchestratorStopping(jobId, state?.Status.ToString() ?? "null");
                    return null;
                }

                List<TombstonedPayload> tombstones = await context.CallActivityAsync<List<TombstonedPayload>>(
                    nameof(GetTombstonedPayloadsActivity),
                    batchSize,
                    new TaskOptions(PurgeActivityRetryPolicy));

                if (tombstones is null || tombstones.Count == 0)
                {
                    // Nothing to purge right now: block on a timer (push-free idle) then check again.
                    await context.CreateTimer(IdleDelay, default);
                    continue;
                }

                List<PayloadPurgeAck> acks = await this.DeleteBatchAsync(context, tombstones);

                if (acks.Count > 0)
                {
                    await context.CallActivityAsync(
                        nameof(AckPurgedPayloadsActivity),
                        acks,
                        new TaskOptions(PurgeActivityRetryPolicy));

                    await context.Entities.CallEntityAsync(
                        input.JobEntityId, nameof(BlobPurgeJob.RecordPurged), (long)acks.Count);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                // A single bad cycle (transient backend/entity/activity failure) must not kill the perpetual
                // loop. Log, back off, then continue so the job self-heals and keeps draining.
                logger.BlobPurgeCycleFailed(ex, jobId);
                await context.CreateTimer(ErrorBackoff, default);
                continue;
            }
        }
    }

    async Task<List<PayloadPurgeAck>> DeleteBatchAsync(
        TaskOrchestrationContext context, List<TombstonedPayload> tombstones)
    {
        List<PayloadPurgeAck> acks = new(tombstones.Count);
        List<Task<DeleteOutcome>> tasks = new();

        foreach (TombstonedPayload tombstone in tombstones)
        {
            tasks.Add(this.DeleteOneAsync(context, tombstone));

            if (tasks.Count >= MaxParallelDeletes)
            {
                await DrainAsync(tasks, acks);
                tasks.Clear();
            }
        }

        if (tasks.Count > 0)
        {
            await DrainAsync(tasks, acks);
        }

        return acks;
    }

    static async Task DrainAsync(List<Task<DeleteOutcome>> tasks, List<PayloadPurgeAck> acks)
    {
        DeleteOutcome[] outcomes = await Task.WhenAll(tasks);
        foreach (DeleteOutcome outcome in outcomes)
        {
            // Acknowledge blobs that were deleted (or already gone) and poison tokens that can never succeed
            // so the backend can hard-delete their rows; transient failures stay tombstoned to retry.
            if (outcome.ShouldAck)
            {
                acks.Add(outcome.Ack);
            }
        }
    }

    async Task<DeleteOutcome> DeleteOneAsync(TaskOrchestrationContext context, TombstonedPayload tombstone)
    {
        BlobDeleteResult result = await context.CallActivityAsync<BlobDeleteResult>(
            nameof(DeleteExternalBlobActivity),
            tombstone.Token);

        return new DeleteOutcome(
            result != BlobDeleteResult.Retry,
            new PayloadPurgeAck(tombstone.PartitionId, tombstone.InstanceKey, tombstone.PayloadId));
    }

    readonly record struct DeleteOutcome(bool ShouldAck, PayloadPurgeAck Ack);
}
