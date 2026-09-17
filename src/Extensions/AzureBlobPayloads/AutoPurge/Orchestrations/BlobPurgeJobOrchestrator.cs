// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// State carried by the eternal purge orchestration across continue-as-new.
/// </summary>
/// <param name="PurgeBatchSize">The maximum tombstones requested per cycle.</param>
/// <param name="PurgedCount">Diagnostic terminal-success count, including duplicates and already-absent payloads.</param>
/// <param name="BackendUnsupported">Whether an explicit enable event is required before retrying the backend.</param>
public sealed record BlobPurgeJobRunRequest(
    int PurgeBatchSize, long PurgedCount = 0, bool BackendUnsupported = false);

/// <summary>
/// Eternal orchestration that fetches, deletes and reports payload tombstones. The backend setting controls
/// fetch eligibility; an empty or disabled fetch idles on a durable timer rather than completing the runner.
/// </summary>
[DurableTask]
public class BlobPurgeJobOrchestrator : TaskOrchestrator<BlobPurgeJobRunRequest, object?>
{
    const int ContinueAsNewFrequency = 5;
    const int DeleteChunkSize = 50;

    // Four chunk activities, each with at most eight concurrent deletes: 32 per cycle.
    const int MaxParallelChunkActivities = 4;
    static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(1);
    static readonly RetryPolicy PurgeActivityRetryPolicy = new(
        maxNumberOfAttempts: 3,
        firstRetryInterval: TimeSpan.FromSeconds(15),
        backoffCoefficient: 2.0,
        maxRetryInterval: TimeSpan.FromSeconds(60))
    {
        HandleFailure = details => !details.IsCausedBy<NotImplementedException>(),
    };

    /// <inheritdoc/>
    public override async Task<object?> RunAsync(TaskOrchestrationContext context, BlobPurgeJobRunRequest input)
    {
        Check.NotNull(input);
        ValidateBatchSize(input.PurgeBatchSize);
        ILogger logger = context.CreateReplaySafeLogger<BlobPurgeJobOrchestrator>();
        int batchSize = input.PurgeBatchSize;
        int processedCycles = 0;
        long purgedCount = input.PurgedCount;
        bool unsupported = input.BackendUnsupported;
        using CancellationTokenSource waiterCancellation = new();
        Task<int> configuration = context.WaitForExternalEvent<int>(
            BlobPurgeConstants.SetBatchSizeEvent, waiterCancellation.Token);

        while (true)
        {
            // Drain delivered and buffered configuration before CAN: event preservation cannot recover a
            // result already delivered to a waiter that the orchestration then ignores.
            while (configuration.IsCompleted)
            {
                batchSize = await configuration;
                ValidateBatchSize(batchSize);
                unsupported = false;
                configuration = context.WaitForExternalEvent<int>(
                    BlobPurgeConstants.SetBatchSizeEvent, waiterCancellation.Token);
            }

            if (processedCycles >= ContinueAsNewFrequency)
            {
                waiterCancellation.Cancel();
                context.ContinueAsNew(
                    new BlobPurgeJobRunRequest(batchSize, PurgedCount: purgedCount, BackendUnsupported: unsupported),
                    preserveUnprocessedEvents: true);
                return null;
            }

            context.SetCustomStatus(new { Status = unsupported ? "BackendUnsupported" : "Running", BatchSize = batchSize, PurgedCount = purgedCount });
            if (unsupported)
            {
                await configuration;
                continue;
            }

            processedCycles++;
            bool wait = false;
            try
            {
                List<LargePayloadTombstone> tombstones = await context.CallActivityAsync<List<LargePayloadTombstone>>(
                    nameof(GetLargePayloadTombstonesActivity), batchSize, new TaskOptions(PurgeActivityRetryPolicy));
                if (tombstones is null || tombstones.Count == 0)
                {
                    // FailedPrecondition (including disabled auto-purge) is mapped to an empty fetch.
                    wait = true;
                }
                else
                {
                    List<LargePayloadPurgeResult> results = await this.DeleteBatchAsync(context, tombstones);
                    await context.CallActivityAsync(
                        nameof(ReportLargePayloadPurgeResultsActivity), results, new TaskOptions(PurgeActivityRetryPolicy));
                    purgedCount += CountDisposition(results, LargePayloadPurgeDisposition.Deleted);
                    wait = results.Count == CountDisposition(results, LargePayloadPurgeDisposition.Retry);
                }
            }
            catch (TaskFailedException ex) when (ex.FailureDetails.IsCausedBy<NotImplementedException>())
            {
                logger.BlobPurgeBackendUnsupported(context.InstanceId, ex.FailureDetails.ErrorMessage);
                unsupported = true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                logger.BlobPurgeCycleFailed(ex, context.InstanceId);
                wait = true;
            }

            if (wait && !configuration.IsCompleted)
            {
                context.SetCustomStatus(new { Status = "Waiting", BatchSize = batchSize, PurgedCount = purgedCount });
                await WaitForTimerOrConfigurationAsync(context, configuration);
            }
        }
    }

    static async Task WaitForTimerOrConfigurationAsync(TaskOrchestrationContext context, Task configuration)
    {
        using CancellationTokenSource timerCancellation = new();
        Task timer = context.CreateTimer(IdleDelay, timerCancellation.Token);
        await Task.WhenAny(timer, configuration);
        if (!timer.IsCompleted)
        {
            timerCancellation.Cancel();
        }

        try
        {
            await timer;
        }
        catch (OperationCanceledException) when (timerCancellation.IsCancellationRequested)
        {
            // The existing configuration waiter interrupted the idle/backoff timer.
        }
    }

    static void ValidateBatchSize(int batchSize) => Check.Argument(
        batchSize > 0 && batchSize <= BlobPurgeConstants.MaxBatchSize, nameof(batchSize), "Purge batch size is out of range.");

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

        // Refuse to attribute dispositions to the wrong rows if the activity violates its positional contract.
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
            results.Add(new LargePayloadPurgeResult(tombstone.TombstoneToken, outcomes[i].Disposition));
        }

        return results;
    }
}
