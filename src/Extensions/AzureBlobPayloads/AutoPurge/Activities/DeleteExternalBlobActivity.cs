// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;
using System.Net;
using Azure;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Activity that deletes a single externalized payload blob given its token, and classifies the attempt as
/// <see cref="LargePayloadPurgeDisposition.Deleted"/>, <see cref="LargePayloadPurgeDisposition.Retry"/>, or
/// <see cref="LargePayloadPurgeDisposition.Quarantined"/> with a stable reason code. Deletion is idempotent,
/// so re-delivered tokens and concurrent workers are safe.
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
/// Per design §7 no log here carries the token or raw exception text - a token exposes the storage account,
/// container, and blob path. Diagnostics are the stable reason enum plus a bounded, sanitized storage error
/// code; the token itself is preserved on the backend's quarantined row.
/// </remarks>
/// <param name="store">The payload store used to delete blobs.</param>
/// <param name="logger">The logger instance.</param>
[DurableTask]
public class DeleteExternalBlobActivity(
    PayloadStore store,
    ILogger<DeleteExternalBlobActivity> logger)
    : TaskActivity<string, BlobPurgeOutcome>
{
    readonly PayloadStore store = Check.NotNull(store);
    readonly ILogger<DeleteExternalBlobActivity> logger = Check.NotNull(logger);

    /// <inheritdoc/>
    public override async Task<BlobPurgeOutcome> RunAsync(TaskActivityContext context, string input)
    {
        Check.NotNullOrEmpty(input, nameof(input));

        BlobPurgeOutcome outcome = await this.DeleteAsync(input);

        switch (outcome.Disposition)
        {
            case LargePayloadPurgeDisposition.Quarantined:
                this.logger.BlobPurgeDeleteQuarantined(outcome.Reason.ToString(), outcome.StorageErrorCode);
                break;
            case LargePayloadPurgeDisposition.Retry:
                this.logger.BlobPurgeDeleteRetryable(outcome.Reason.ToString(), outcome.StorageErrorCode);
                break;
            case LargePayloadPurgeDisposition.Deleted
                when outcome.Reason == LargePayloadPurgeReason.BlobNotStoreOwned:
                this.logger.BlobPurgeBlobNotStoreOwned();
                break;
        }

        return outcome;
    }

    /// <summary>
    /// Extracts a bounded, sanitized storage error code for diagnostics. The service's own error code (for
    /// example <c>BlobNotFound</c>) is a fixed vocabulary and the numeric status is the fallback, so neither
    /// can carry a token or raw exception text.
    /// </summary>
    static string SanitizeErrorCode(RequestFailedException exception) =>
        string.IsNullOrEmpty(exception.ErrorCode)
            ? exception.Status.ToString(CultureInfo.InvariantCulture)
            : exception.ErrorCode;

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
            return new BlobPurgeOutcome(
                LargePayloadPurgeDisposition.Quarantined, LargePayloadPurgeReason.LegacyV1Token);
        }

        if (!token.StartsWith(BlobPayloadStore.TokenPrefixV2, StringComparison.Ordinal))
        {
            // An unrecognized prefix is most likely a token written by a newer SDK than this worker runs. That
            // recovers after an upgrade, so it earns a deferral rather than quarantine.
            return new BlobPurgeOutcome(
                LargePayloadPurgeDisposition.Retry, LargePayloadPurgeReason.UnsupportedTokenVersion);
        }

        try
        {
            PayloadDeleteOutcome outcome = await this.store.DeleteAsync(token, CancellationToken.None);
            return outcome switch
            {
                PayloadDeleteOutcome.Deleted => new BlobPurgeOutcome(
                    LargePayloadPurgeDisposition.Deleted, LargePayloadPurgeReason.BlobDeleted),
                PayloadDeleteOutcome.AlreadyAbsent => new BlobPurgeOutcome(
                    LargePayloadPurgeDisposition.Deleted, LargePayloadPurgeReason.BlobAlreadyAbsent),

                // The blob exists but this store never wrote it, so it was left untouched. That is an expected
                // outcome, not a defect - the token text merely matched the v2 grammar - and quarantining it
                // would fill the quarantine set with non-defects. The tombstone is still resolved, because a
                // blob the store does not own is not the store's to delete.
                _ => new BlobPurgeOutcome(
                    LargePayloadPurgeDisposition.Deleted, LargePayloadPurgeReason.BlobNotStoreOwned),
            };
        }
        catch (ArgumentException)
        {
            // The prefix gate above proves this is a v2 token, so the only remaining decode failure is a v2
            // body that does not parse. The SDK and backend control both sides of the protocol, so that
            // indicates a producer, corruption, or compatibility bug; retrying can never fix it.
            return new BlobPurgeOutcome(
                LargePayloadPurgeDisposition.Quarantined, LargePayloadPurgeReason.MalformedToken);
        }
        catch (NotSupportedException)
        {
            // The registered store does not implement deletion. Every payload would fail the same way, so the
            // work is kept recoverable until an operator registers a store that can delete.
            return new BlobPurgeOutcome(
                LargePayloadPurgeDisposition.Retry, LargePayloadPurgeReason.StoreCannotDelete);
        }
        catch (PayloadStorageException)
        {
            // The token is well formed but points at a storage account this worker's credential cannot reach
            // (account-key auth is account-specific). Recoverable after a configuration or credential change,
            // so it is deferred rather than discarded.
            return new BlobPurgeOutcome(
                LargePayloadPurgeDisposition.Retry, LargePayloadPurgeReason.StorageAccountUnreachable);
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.BadRequest)
        {
            // Storage rejected a request generated from a well-formed token as permanently invalid (for
            // example InvalidUri / InvalidResourceName). Retrying can never succeed.
            return new BlobPurgeOutcome(
                LargePayloadPurgeDisposition.Quarantined,
                LargePayloadPurgeReason.InvalidStorageRequest,
                SanitizeErrorCode(ex));
        }
        catch (RequestFailedException ex) when (
            ex.Status == (int)HttpStatusCode.Unauthorized || ex.Status == (int)HttpStatusCode.Forbidden)
        {
            // Authorization can be transient or fixed by reconfiguration, so it stays recoverable rather than
            // dropping data an operator can still reclaim.
            return new BlobPurgeOutcome(
                LargePayloadPurgeDisposition.Retry,
                LargePayloadPurgeReason.StorageAuthorizationFailed,
                SanitizeErrorCode(ex));
        }
        catch (RequestFailedException ex)
        {
            // Throttling, 5xx, and anything else the service reported, including a failed If-Match on the
            // ownership check: transient by default.
            return new BlobPurgeOutcome(
                LargePayloadPurgeDisposition.Retry,
                LargePayloadPurgeReason.TransientStorageFailure,
                SanitizeErrorCode(ex));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Timeouts, cancellation, and network failures. A blob is never dropped on an uncertain error.
            return new BlobPurgeOutcome(
                LargePayloadPurgeDisposition.Retry, LargePayloadPurgeReason.TransientStorageFailure);
        }
    }
}
