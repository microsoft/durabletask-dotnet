// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Activity that deletes a single externalized payload blob given its token. Deletion is idempotent, so
/// re-delivered tokens and concurrent workers are safe. On failure the payload is left tombstoned so a later
/// purge cycle can retry it.
/// </summary>
/// <param name="store">The payload store used to delete blobs.</param>
/// <param name="logger">The logger instance.</param>
[DurableTask]
public class DeleteExternalBlobActivity(
    PayloadStore store,
    ILogger<DeleteExternalBlobActivity> logger)
    : TaskActivity<string, bool>
{
    readonly PayloadStore store = Check.NotNull(store);
    readonly ILogger<DeleteExternalBlobActivity> logger = Check.NotNull(logger);

    /// <inheritdoc/>
    public override async Task<bool> RunAsync(TaskActivityContext context, string input)
    {
        Check.NotNullOrEmpty(input, nameof(input));

        try
        {
            await this.store.DeleteAsync(input, CancellationToken.None);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Leave the payload tombstoned so the backend re-streams it on a later cycle; a single bad token
            // must not fail the whole batch.
            this.logger.BlobPurgeDeleteFailed(ex, input);
            return false;
        }
    }
}
