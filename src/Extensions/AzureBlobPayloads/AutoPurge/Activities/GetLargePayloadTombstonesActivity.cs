// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Activity that fetches a bounded batch of due large-payload tombstones from the backend for the auto-purge
/// job to delete.
/// </summary>
/// <param name="client">The purge client used to query the backend for tombstones.</param>
/// <param name="logger">The logger instance.</param>
[DurableTask]
internal sealed class GetLargePayloadTombstonesActivity(
    ILargePayloadPurgeClient client,
    ILogger<GetLargePayloadTombstonesActivity> logger)
    : TaskActivity<int, List<LargePayloadTombstone>>
{
    readonly ILargePayloadPurgeClient client = Check.NotNull(client);
    readonly ILogger<GetLargePayloadTombstonesActivity> logger = Check.NotNull(logger);

    /// <inheritdoc/>
    public override async Task<List<LargePayloadTombstone>> RunAsync(TaskActivityContext context, int input)
    {
        int limit = input;
        List<LargePayloadTombstone> tombstones =
            await this.client.GetLargePayloadTombstonesAsync(limit, CancellationToken.None);
        this.logger.BlobPurgeFetchedTombstones(tombstones.Count);
        return tombstones;
    }
}
