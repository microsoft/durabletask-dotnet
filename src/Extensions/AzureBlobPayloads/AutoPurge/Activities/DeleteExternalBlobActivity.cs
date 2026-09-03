// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;
using System.Net;
using Azure;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Activity that deletes a chunk of externalized payload blobs given their tokens, classifying each attempt as
/// <see cref="LargePayloadPurgeDisposition.Deleted"/>, <see cref="LargePayloadPurgeDisposition.Retry"/>, or
/// <see cref="LargePayloadPurgeDisposition.Quarantined"/> and returning one outcome per token, positionally
/// aligned to the input. Deleting a whole chunk in a single activity call is what keeps orchestration history
/// small. Deletion is idempotent, so re-delivered tokens, a retried chunk, and concurrent workers are all safe.
/// </summary>
/// <remarks>
/// The split between retry and quarantine is whether the failure can self-heal, verified against the
/// Azure.Storage.Blobs / Azure.Core exception model (not assumed):
/// <list type="bullet">
/// <item>
/// The Azure SDK already retries transient failures internally (connection errors plus HTTP
/// 408/429/500/502/503/504, with exponential backoff), so any exception that escapes
/// <see cref="PayloadStore.DeleteAsync"/> means those built-in retries were already exhausted. It is still
/// classified as retryable, because the backend - not this activity - owns retry scheduling and can defer the
/// row past a storage outage.
/// </item>
/// <item>
/// Quarantine is reserved for deterministic failures and protocol violations that retrying can never fix: a
/// known version prefix whose body does not parse, a request storage rejected as permanently invalid, and a
/// legacy v1 token. Quarantine preserves the row and its token as durable evidence, so a permanent failure
/// neither blocks the queue nor destroys the only record of the blob.
/// </item>
/// <item>
/// A blob is never deleted on an uncertain error, and a single bad token never fails the whole batch: a
/// failure is returned as a disposition rather than thrown.
/// </item>
/// </list>
/// The reported result carries the disposition alone, so every branch below logs its cause where the cause is
/// still exact, rather than deriving it afterwards from a value that crossed the wire. That log is the only
/// record of why an attempt failed. Per design §7 it still carries neither the token nor raw exception text -
/// a token exposes the storage account, container, and blob path - so the cause is a bounded classification
/// string plus a bounded, sanitized storage error code. The token itself is preserved on the backend's
/// quarantined row.
/// </remarks>
/// <param name="store">The payload store used to delete blobs.</param>
/// <param name="logger">The logger instance.</param>
[DurableTask]
public class DeleteExternalBlobActivity(
    PayloadStore store,
    ILogger<DeleteExternalBlobActivity> logger)
    : TaskActivity<List<string>, List<BlobPurgeOutcome>>
{
    // Concurrency cap for the deletes WITHIN one chunk. The orchestrator runs at most
    // BlobPurgeJobOrchestrator.MaxParallelChunkActivities (4) of these activities at once, so the total number
    // of storage deletes in flight across the worker is 4 x 8 = 32 - identical to the flat cap this chunked
    // design replaced. That product is the real budget: if either factor changes the other must move to keep it
    // at 32, or the worker will either starve throughput or multiply into hundreds of concurrent storage calls
    // (e.g. 20 chunks x 32 = 640).
    const int MaxParallelDeletesPerChunk = 8;

    readonly PayloadStore store = Check.NotNull(store);
    readonly ILogger<DeleteExternalBlobActivity> logger = Check.NotNull(logger);

    /// <summary>
    /// Gets or sets the wall-clock ceiling for a single blob delete. The store's own retry policy allows up to
    /// 8 attempts against a 2-minute network timeout (~18 minutes worst case) for one blob, and a chunk awaits
    /// its slowest delete, so without a bound one hung blob would hold a concurrency slot for many minutes and
    /// stall the whole wave. Capping it well under the activity's own 15s/30s/60s retry cadence means a stuck
    /// delete gives up, surfaces as <see cref="OperationCanceledException"/>, and is classified
    /// <see cref="LargePayloadPurgeDisposition.Retry"/> for the backend to defer - rather than pinning the slot.
    /// It is settable only so a test can shrink it; it is never reconfigured at runtime.
    /// </summary>
    internal TimeSpan SingleDeleteTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <inheritdoc/>
    public override async Task<List<BlobPurgeOutcome>> RunAsync(TaskActivityContext context, List<string> input)
    {
        Check.NotNull(input, nameof(input));

        // Write each outcome at its token's INDEX, never in completion order: the deletes below run
        // concurrently and the orchestrator zips these back onto tombstones positionally, so a delete finishing
        // out of order must not shift a disposition onto the wrong row.
        BlobPurgeOutcome[] outcomes = new BlobPurgeOutcome[input.Count];

        // A single bad token never fails its peers: DeleteAsync returns a disposition on every branch instead
        // of throwing (its catch-all absorbs everything but OutOfMemory/StackOverflow), so Task.WhenAll never
        // observes a fault from a classified failure and the other tokens in the chunk still complete and report.
        using SemaphoreSlim gate = new(MaxParallelDeletesPerChunk, MaxParallelDeletesPerChunk);
        Task[] deletes = new Task[input.Count];
        for (int i = 0; i < input.Count; i++)
        {
            deletes[i] = DeleteAtAsync(i);
        }

        await Task.WhenAll(deletes);

        return new List<BlobPurgeOutcome>(outcomes);

        async Task DeleteAtAsync(int index)
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                outcomes[index] = await this.DeleteAsync(input[index]).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }
    }

    /// <summary>
    /// Extracts a bounded, sanitized storage error code for diagnostics. The service's own error code (for
    /// example <c>BlobNotFound</c>) is a fixed vocabulary and the numeric status is the fallback, so neither
    /// can carry a token or raw exception text.
    /// </summary>
    static string SanitizeErrorCode(RequestFailedException exception)
    {
        // Pattern-matched rather than string.IsNullOrEmpty: on netstandard2.0 that method carries no
        // [NotNullWhen(false)] annotation, so flow analysis cannot prove the else branch is non-null and warns.
        // A constant pattern is analyzed by the compiler itself and so behaves the same on every target.
        string? errorCode = exception.ErrorCode;
        return errorCode is null or ""
            ? exception.Status.ToString(CultureInfo.InvariantCulture)
            : errorCode;
    }

    async Task<BlobPurgeOutcome> DeleteAsync(string token)
    {
        // Classify the token's version prefix before consulting the store. The store reports every token it
        // cannot decode as the same ArgumentException, but the three cases have opposite dispositions, so they
        // are separated here, where the prefix is still visible.
        if (token.StartsWith(BlobPayloadStore.TokenPrefixV1, StringComparison.Ordinal))
        {
            // A v1 token carries a container *name* but not the storage account, so a delete against the
            // currently-configured account cannot be verified: if the store has since been repointed,
            // DeleteIfExists returns false and the purge would falsely report success while the real blob
            // survives in the old account. Retrying cannot fix that, and a success-shaped discard would destroy
            // the only durable record of the blob, so the row is quarantined instead - the backend preserves
            // its token as evidence and stops polling it. The backend excludes v1 at insertion time, so
            // reaching this branch is an invariant violation rather than an expected path.
            this.logger.BlobPurgeDeleteQuarantined("LegacyV1Token", null);
            return new BlobPurgeOutcome(LargePayloadPurgeDisposition.Quarantined);
        }

        if (!token.StartsWith(BlobPayloadStore.TokenPrefixV2, StringComparison.Ordinal))
        {
            // An unrecognized prefix is most likely a token written by a newer SDK than this worker runs. That
            // recovers after an upgrade, so it earns a deferral rather than quarantine. Quarantine is
            // permanent and requires an operator to unwind; a deferral only leaves the row idle and visible,
            // so an unrecognized token is deliberately kept on the recoverable side of that asymmetry.
            this.logger.BlobPurgeDeleteRetryable("UnsupportedTokenVersion", null);
            return new BlobPurgeOutcome(LargePayloadPurgeDisposition.Retry);
        }

        try
        {
            // Bound the delete's wall-clock time (see SingleDeleteTimeout). If it elapses, the token cancels the
            // store call, which surfaces as OperationCanceledException and is classified retryable by the
            // catch-all below - so a hung blob yields to the backend's deferral instead of pinning its slot.
            // TaskActivityContext exposes no ambient cancellation token to link, so this timeout is the only
            // cancellation source; host shutdown is handled by the worker tearing the activity down.
            using CancellationTokenSource timeout = new(this.SingleDeleteTimeout);
            PayloadDeleteOutcome outcome = await this.store.DeleteAsync(token, timeout.Token);

            // The blob exists but this store never wrote it, so it was left untouched. That is an expected
            // outcome, not a defect - the token text merely matched the v2 grammar - and quarantining it would
            // fill the quarantine set with non-defects. The tombstone is still resolved, because a blob the
            // store does not own is not the store's to delete.
            if (outcome == PayloadDeleteOutcome.NotStoreOwned)
            {
                this.logger.BlobPurgeBlobNotStoreOwned();
            }

            // Deleted, AlreadyAbsent, and NotStoreOwned are all terminal successes: none can be improved by
            // trying again.
            return new BlobPurgeOutcome(LargePayloadPurgeDisposition.Deleted);
        }
        catch (ArgumentException)
        {
            // The prefix gate above proves this is a v2 token, so the only remaining decode failure is a v2
            // body that does not parse. The SDK and backend control both sides of the protocol, so that
            // indicates a producer, corruption, or compatibility bug; retrying can never fix it.
            this.logger.BlobPurgeDeleteQuarantined("MalformedToken", null);
            return new BlobPurgeOutcome(LargePayloadPurgeDisposition.Quarantined);
        }
        catch (NotSupportedException)
        {
            // The registered store does not implement deletion. Every payload would fail the same way, so the
            // work is kept recoverable until an operator registers a store that can delete.
            this.logger.BlobPurgeDeleteRetryable("StoreCannotDelete", null);
            return new BlobPurgeOutcome(LargePayloadPurgeDisposition.Retry);
        }
        catch (PayloadStorageException)
        {
            // The token is well formed but points at a storage account this worker's credential cannot reach
            // (account-key auth is account-specific). Recoverable after a configuration or credential change,
            // so it is deferred rather than discarded.
            this.logger.BlobPurgeDeleteRetryable("StorageAccountUnreachable", null);
            return new BlobPurgeOutcome(LargePayloadPurgeDisposition.Retry);
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.BadRequest)
        {
            // Storage rejected a request generated from a well-formed token as permanently invalid (for
            // example InvalidUri / InvalidResourceName). Retrying can never succeed.
            this.logger.BlobPurgeDeleteQuarantined("InvalidStorageRequest", SanitizeErrorCode(ex));
            return new BlobPurgeOutcome(LargePayloadPurgeDisposition.Quarantined);
        }
        catch (RequestFailedException ex) when (
            ex.Status == (int)HttpStatusCode.Unauthorized || ex.Status == (int)HttpStatusCode.Forbidden)
        {
            // Authorization can be transient or fixed by reconfiguration, so it stays recoverable rather than
            // dropping data an operator can still reclaim.
            this.logger.BlobPurgeDeleteRetryable("StorageAuthorizationFailed", SanitizeErrorCode(ex));
            return new BlobPurgeOutcome(LargePayloadPurgeDisposition.Retry);
        }
        catch (RequestFailedException ex)
        {
            // Throttling, 5xx, and anything else the service reported, including a failed If-Match on the
            // ownership check: transient by default.
            this.logger.BlobPurgeDeleteRetryable("TransientStorageFailure", SanitizeErrorCode(ex));
            return new BlobPurgeOutcome(LargePayloadPurgeDisposition.Retry);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Timeouts, cancellation, and network failures. A blob is never dropped on an uncertain error.
            // Storage reported no code here, so the exception's type name is appended to the cause: it is a
            // bounded value that cannot carry a token, and it is the only thing separating a timeout from a
            // cancellation or a DNS failure now that no classification crosses the wire.
            this.logger.BlobPurgeDeleteRetryable($"UnexpectedFailure:{ex.GetType().Name}", null);
            return new BlobPurgeOutcome(LargePayloadPurgeDisposition.Retry);
        }
    }
}
