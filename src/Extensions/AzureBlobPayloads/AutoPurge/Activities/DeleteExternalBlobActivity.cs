// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Activity that deletes a single externalized payload blob given its token. Deletion is idempotent, so
/// re-delivered tokens and concurrent workers are safe.
/// </summary>
/// <remarks>
/// Outcome classification, verified against the Azure.Storage.Blobs / Azure.Core exception model (not
/// assumed):
/// <list type="bullet">
/// <item>
/// The Azure SDK already retries transient failures internally (connection errors plus HTTP
/// 408/429/500/502/503/504, with exponential backoff), so any exception that escapes
/// <see cref="PayloadStore.DeleteAsync"/> means those built-in retries were already exhausted.
/// </item>
/// <item>
/// Legacy <c>blob:v1:</c> tokens are discarded without a delete attempt: a v1 token carries only a container
/// name, not the storage account, so a delete against the currently-configured account cannot be verified - if
/// the store has been repointed the delete would report success while the real blob survives elsewhere. The
/// backend ack protocol has no "skip" status (an un-acked row is re-served every cycle), so the token is acked
/// to keep the pipeline moving and logged at error level as the operator's recovery pointer. A current backend
/// hard-deletes v1 rows instead of tombstoning them, so this branch is a defensive guard for an older backend
/// build or a row tombstoned before that fix, not the expected path.
/// </item>
/// <item>
/// Permanent failures are discarded (acked so the backend clears the row) because retrying can never succeed:
/// an <see cref="ArgumentException"/> from the store's token decode - a genuinely malformed or unrecognized
/// token (v1 tokens are gated out above and never reach the store); a
/// <see cref="RequestFailedException"/> with <see cref="RequestFailedException.Status"/> 400 (for example
/// InvalidUri / InvalidResourceName when the decoded blob name violates Azure naming rules); and a
/// <see cref="PayloadStorageException"/> when a v2 token points at a storage account the configured credential
/// cannot reach (connection-string / account-key auth is account-specific). The backend batch is cursor-less,
/// so an undroppable row would otherwise re-stream every cycle and block the pipeline head-of-line; the
/// account-unreachable case is logged at error level so an operator can reconcile it.
/// </item>
/// <item>
/// Everything else is treated as transient and leaves the payload tombstoned to retry on a later cycle:
/// throttling / 5xx that outlived the SDK's retries, 403 authorization failures (which need an operator
/// credential fix rather than dropping data), timeouts / cancellation, and a <see cref="NotSupportedException"/>
/// from a misconfigured store that cannot delete at all (retried, not dropped, so the work is recoverable once
/// a deleting store is registered). A blob is never dropped on an uncertain error, and a single bad token never
/// fails the whole batch.
/// </item>
/// </list>
/// </remarks>
/// <param name="store">The payload store used to delete blobs.</param>
/// <param name="logger">The logger instance.</param>
[DurableTask]
public class DeleteExternalBlobActivity(
    PayloadStore store,
    ILogger<DeleteExternalBlobActivity> logger)
    : TaskActivity<string, BlobDeleteResult>
{
    readonly PayloadStore store = Check.NotNull(store);
    readonly ILogger<DeleteExternalBlobActivity> logger = Check.NotNull(logger);

    /// <inheritdoc/>
    public override async Task<BlobDeleteResult> RunAsync(TaskActivityContext context, string input)
    {
        Check.NotNullOrEmpty(input, nameof(input));

        if (input.StartsWith(BlobPayloadStore.TokenPrefixV1, StringComparison.Ordinal))
        {
            // Auto-purge deliberately does not act on legacy v1 tokens. A v1 token carries only a container
            // *name* - not the storage account - so a delete against the currently-configured account cannot be
            // verified: if the store has since been repointed, DeleteIfExistsAsync returns false and the purge
            // would silently report success while the real blob survives in the old account. Rather than delete
            // on an unverifiable pointer, the token is discarded. The backend ack protocol carries no "skip"
            // status (PayloadPurgeAck is just partition/instance/payload id, and an un-acked row is re-served by
            // an uncursored TOP(N) query every cycle), so declining without acking would permanently block the
            // pipeline. The full token is logged at error level so it remains a recoverable pointer. A current
            // backend hard-deletes v1 rows instead of tombstoning them, so reaching this branch means an older
            // backend build or a row tombstoned before that fix - it is a defensive guard, not the expected path.
            this.logger.BlobPurgeDeleteV1TokenUnsupported(input);
            return BlobDeleteResult.Discarded;
        }

        try
        {
            await this.store.DeleteAsync(input, CancellationToken.None);
            return BlobDeleteResult.Deleted;
        }
        catch (ArgumentException ex)
        {
            // The token is malformed or points at a different container; it can never succeed. Discard it so
            // the backend clears the row instead of re-streaming the same poison token every cycle.
            this.logger.BlobPurgeDeleteDiscarded(ex, input);
            return BlobDeleteResult.Discarded;
        }
        catch (RequestFailedException ex) when (ex.Status == 400)
        {
            // Service rejected the request as permanently invalid (e.g. InvalidUri / InvalidResourceName - the
            // decoded blob name violates Azure naming rules). Retrying can never succeed, so discard it like a
            // poison token: ack so the backend clears the row instead of re-streaming it forever.
            this.logger.BlobPurgeDeleteDiscarded(ex, input);
            return BlobDeleteResult.Discarded;
        }
        catch (NotSupportedException ex)
        {
            // The registered store does not implement deletion (PayloadStore.DeleteAsync is virtual and its base
            // implementation throws). This is a misconfiguration, not poison data: every payload would fail the
            // same way, so acking would hard-delete the backend's entire record of what still needs cleanup while
            // every blob survives. Keep the payload tombstoned so the work is recoverable once an operator
            // registers a store that can delete.
            this.logger.BlobPurgeDeleteNotSupported(ex, input);
            return BlobDeleteResult.Retry;
        }
        catch (PayloadStorageException ex)
        {
            // The token is well-formed but points at a storage account this worker's credential cannot reach
            // (cross-account without AAD). Retrying can never succeed from this process, and because the backend
            // streams tombstones with an uncursored TOP(N) query, leaving it un-acked would re-serve the same
            // token every cycle and permanently block the purge pipeline. Discard it so the row is cleared, and
            // log at Error so an operator can reclaim the orphaned blob out-of-band.
            this.logger.BlobPurgeDeleteUnreachable(ex, input);
            return BlobDeleteResult.Discarded;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Transient failure: leave the payload tombstoned so a later purge cycle can retry it. A single
            // bad token must not fail the whole batch.
            this.logger.BlobPurgeDeleteFailed(ex, input);
            return BlobDeleteResult.Retry;
        }
    }
}
