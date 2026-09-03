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
/// Perpetual orchestrator that drains due large-payload tombstones from the backend, deletes their blobs with
/// capped parallelism, and reports every outcome so the backend can resolve, reschedule, or quarantine each
/// row. It idles on a timer when there is nothing to purge and continues-as-new periodically to keep its
/// history small.
/// </summary>
[DurableTask]
public class BlobPurgeJobOrchestrator : TaskOrchestrator<BlobPurgeJobRunRequest, object?>
{
    const int ContinueAsNewFrequency = 5;

    // Tombstones are deleted a chunk at a time: one activity call handles DeleteChunkSize tokens instead of a
    // single token, which is what keeps orchestration history small. A full 1000-row cycle becomes 1000 / 50 =
    // 20 activity calls (~40 history events) instead of 1000 calls (~2000 events) - a 50x reduction - and drains
    // in ceil(20 / 4) = 5 waves instead of 32.
    const int DeleteChunkSize = 50;

    // How many chunk-delete activities run concurrently. TOTAL concurrent storage deletes are this times the
    // activity's own MaxParallelDeletesPerChunk (8): 4 x 8 = 32, exactly the flat cap the per-token design used.
    // The product is the budget - moving one factor without the other either starves throughput or multiplies
    // into hundreds of concurrent storage calls.
    const int MaxParallelChunkActivities = 4;
    static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(1);
    static readonly TimeSpan ErrorBackoff = TimeSpan.FromMinutes(1);

    // Retry policy for the purge activities: 3 attempts with exponential backoff (15s, 30s, capped at 60s).
    static readonly RetryPolicy PurgeActivityRetryPolicy = new(
        maxNumberOfAttempts: 3,
        firstRetryInterval: TimeSpan.FromSeconds(15),
        backoffCoefficient: 2.0,
        maxRetryInterval: TimeSpan.FromSeconds(60))
    {
        // A NotImplementedException means the backend does not implement the purge RPCs (mixed rollout / stale
        // emulator). That cannot be fixed by retrying, so short-circuit the ~45s retry budget and let the
        // failure surface on the first attempt. RunAsync catches it and disables the job instead of looping.
        HandleFailure = details => !details.IsCausedBy<NotImplementedException>(),
    };

    /// <inheritdoc/>
    public override async Task<object?> RunAsync(TaskOrchestrationContext context, BlobPurgeJobRunRequest input)
    {
        ILogger logger = context.CreateReplaySafeLogger<BlobPurgeJobOrchestrator>();
        string jobId = input.JobEntityId.Key;

        int batchSize = input.PurgeBatchSize;
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
                // Stop cleanly if the job has been stopped or removed. BlobPurgeJob.Stop is what makes this
                // reachable: it moves the entity off Active without touching this orchestrator at all, so
                // shutdown is cooperative - the in-flight cycle finishes and the loop exits on its own terms
                // rather than being terminated part-way through a batch of deletes.
                // input: null is named deliberately. A bare positional null binds to the (id, name, options)
                // overload instead, which reads as if an input were being passed when it is not.
                BlobPurgeJobState? state = await context.Entities.CallEntityAsync<BlobPurgeJobState?>(
                    input.JobEntityId, nameof(BlobPurgeJob.Get), input: null);

                if (state is null || state.Status != BlobPurgeJobStatus.Active)
                {
                    logger.BlobPurgeJobOrchestratorStopping(jobId, state?.Status.ToString() ?? "null");
                    return null;
                }

                // Take the batch size from the entity rather than from this orchestrator's input. A perpetual
                // orchestrator outlives configuration changes: its input is fixed when it is created and is
                // carried verbatim through every continue-as-new, so using input.PurgeBatchSize would pin the
                // value written by the very first Create for the entire life of the job. Re-reading it from the
                // state fetch this cycle already performs costs no extra call and is what lets a changed batch
                // size actually take effect. That matters because a batch size the backend rejects fails every
                // fetch: without this the job would be wedged with no recovery short of deleting the entity.
                //
                // Fall back to the input when the stored value is not positive. An entity written by an older
                // build carries no batch size at all, and asking the backend for zero rows every cycle would be
                // a silent, permanent stall.
                int cycleBatchSize = state.PurgeBatchSize > 0 ? state.PurgeBatchSize : batchSize;

                List<LargePayloadTombstone> tombstones = await context.CallActivityAsync<List<LargePayloadTombstone>>(
                    nameof(GetLargePayloadTombstonesActivity),
                    cycleBatchSize,
                    new TaskOptions(PurgeActivityRetryPolicy));

                if (tombstones is null || tombstones.Count == 0)
                {
                    // Nothing to purge right now: block on a timer (push-free idle) then check again.
                    await context.CreateTimer(IdleDelay, default);
                    continue;
                }

                List<LargePayloadPurgeResult> results = await this.DeleteBatchAsync(context, tombstones);

                // Every attempted row produces a result, including the retryable ones: the backend owns retry
                // scheduling, so it needs to hear about a failure to defer the row. Reporting unconditionally
                // is what keeps a failing row from being re-served unchanged on the very next cycle.
                await context.CallActivityAsync(
                    nameof(ReportLargePayloadPurgeResultsActivity),
                    results,
                    new TaskOptions(PurgeActivityRetryPolicy));

                // Two different questions, deliberately not conflated. Progress counts only payloads that were
                // actually purged; the backoff decision asks whether ANY row left the retry queue, because a
                // quarantined row also stops being re-served even though nothing was reclaimed.
                int purged = CountDisposition(results, LargePayloadPurgeDisposition.Deleted);
                int resolved = results.Count - CountDisposition(results, LargePayloadPurgeDisposition.Retry);

                if (purged > 0)
                {
                    await context.Entities.CallEntityAsync(
                        input.JobEntityId, nameof(BlobPurgeJob.RecordPurged), (long)purged);
                }

                if (resolved == 0)
                {
                    // The whole batch came back retryable (e.g. a storage outage or throttling). Deletes report
                    // failure as a return value rather than an exception, so no activity retry policy applies on
                    // that path. Continuing immediately would refetch and re-attempt in a tight loop for as long
                    // as the outage lasts, so back off before the next cycle.
                    await context.CreateTimer(ErrorBackoff, default);
                }
            }
            catch (TaskFailedException ex) when (ex.FailureDetails.IsCausedBy<NotImplementedException>())
            {
                // The backend does not implement the large-payload purge RPCs (an older backend build or a
                // stale local emulator image). Retrying cannot help, so disable the job durably and exit the
                // perpetual loop cleanly instead of logging a generic failure and backing off forever.
                // MarkUnsupported is AWAITED, not signalled, so the disable is committed to the entity before we
                // return. Nothing restarts the job automatically: it stays Unsupported until a caller
                // explicitly enables auto-purge again, and that call's Create revives it. This is deliberately
                // a distinct diagnostic from BlobPurgeCycleFailed below.
                logger.BlobPurgeBackendUnsupported(jobId, ex.FailureDetails.ErrorMessage);
                await context.Entities.CallEntityAsync(
                    input.JobEntityId, nameof(BlobPurgeJob.MarkUnsupported), ex.FailureDetails.ErrorMessage);
                return null;
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

    static int CountDisposition(
        List<LargePayloadPurgeResult> results, LargePayloadPurgeDisposition disposition)
    {
        int count = 0;
        foreach (LargePayloadPurgeResult result in results)
        {
            if (result.Disposition == disposition)
            {
                count++;
            }
        }

        return count;
    }

    static async Task DrainAsync(
        List<Task<List<LargePayloadPurgeResult>>> tasks, List<LargePayloadPurgeResult> results)
    {
        List<LargePayloadPurgeResult>[] completed = await Task.WhenAll(tasks);
        foreach (List<LargePayloadPurgeResult> chunkResults in completed)
        {
            results.AddRange(chunkResults);
        }
    }

    async Task<List<LargePayloadPurgeResult>> DeleteBatchAsync(
        TaskOrchestrationContext context, List<LargePayloadTombstone> tombstones)
    {
        List<LargePayloadPurgeResult> results = new(tombstones.Count);
        List<Task<List<LargePayloadPurgeResult>>> inFlight = new();

        for (int start = 0; start < tombstones.Count; start += DeleteChunkSize)
        {
            int count = Math.Min(DeleteChunkSize, tombstones.Count - start);
            List<LargePayloadTombstone> chunk = tombstones.GetRange(start, count);
            inFlight.Add(this.DeleteChunkAsync(context, chunk));

            if (inFlight.Count >= MaxParallelChunkActivities)
            {
                await DrainAsync(inFlight, results);
                inFlight.Clear();
            }
        }

        if (inFlight.Count > 0)
        {
            await DrainAsync(inFlight, results);
        }

        return results;
    }

    async Task<List<LargePayloadPurgeResult>> DeleteChunkAsync(
        TaskOrchestrationContext context, List<LargePayloadTombstone> chunk)
    {
        List<string> tokens = new(chunk.Count);
        foreach (LargePayloadTombstone tombstone in chunk)
        {
            tokens.Add(tombstone.PayloadToken);
        }

        List<BlobPurgeOutcome> outcomes = await context.CallActivityAsync<List<BlobPurgeOutcome>>(
            nameof(DeleteExternalBlobActivity),
            tokens,
            new TaskOptions(PurgeActivityRetryPolicy));

        // The activity contract is one outcome per input token, positionally aligned. Assert that before zipping
        // so a broken contract fails loudly here instead of silently pinning each disposition onto the wrong row
        // - which would tell the backend to delete, retry, or quarantine the wrong payloads.
        if (outcomes is null || outcomes.Count != chunk.Count)
        {
            throw new InvalidOperationException(
                $"The blob delete activity returned {outcomes?.Count ?? 0} outcomes for a chunk of {chunk.Count} " +
                "tokens; expected exactly one per token. Refusing to attribute dispositions to the wrong rows.");
        }

        List<LargePayloadPurgeResult> results = new(chunk.Count);
        for (int i = 0; i < chunk.Count; i++)
        {
            LargePayloadTombstone tombstone = chunk[i];

            // The tombstone token is echoed back unchanged: it is opaque to the SDK and is the only thing that
            // tells the backend which row this disposition belongs to. Retry scheduling is the backend's job,
            // so no next-attempt time is computed here.
            results.Add(new LargePayloadPurgeResult(tombstone.TombstoneToken, outcomes[i].Disposition));
        }

        return results;
    }
}
