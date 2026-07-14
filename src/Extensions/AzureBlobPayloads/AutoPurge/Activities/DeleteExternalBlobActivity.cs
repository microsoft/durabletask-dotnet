// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Activity that deletes a single externalized payload blob given its token. Deletion is idempotent, so
/// re-delivered tokens and concurrent workers are safe. Malformed (poison) tokens are discarded so they get
/// acknowledged instead of retried forever; transient failures leave the payload tombstoned to retry.
/// </summary>
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
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Transient failure: leave the payload tombstoned so a later purge cycle can retry it. A single
            // bad token must not fail the whole batch.
            this.logger.BlobPurgeDeleteFailed(ex, input);
            return BlobDeleteResult.Retry;
        }
    }
}
