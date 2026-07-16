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
/// Permanent failures are discarded (acked so the backend clears the row): an <see cref="ArgumentException"/>
/// from the store's own token decode / container-mismatch check (thrown client-side before any network call),
/// and a <see cref="RequestFailedException"/> with <see cref="RequestFailedException.Status"/> 400 (for
/// example InvalidUri / InvalidResourceName when the decoded blob name violates Azure naming rules). Retrying
/// either can never succeed.
/// </item>
/// <item>
/// Everything else is treated as transient and leaves the payload tombstoned to retry on a later cycle:
/// throttling / 5xx that outlived the SDK's retries, 403 authorization failures (which need an operator
/// credential fix rather than dropping data), and timeouts / cancellation. A blob is never dropped on an
/// uncertain error, and a single bad token never fails the whole batch.
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
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Transient failure: leave the payload tombstoned so a later purge cycle can retry it. A single
            // bad token must not fail the whole batch.
            this.logger.BlobPurgeDeleteFailed(ex, input);
            return BlobDeleteResult.Retry;
        }
    }
}
