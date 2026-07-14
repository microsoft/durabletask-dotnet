// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Activity that fetches a batch of tombstoned payloads from the backend for the auto-purge job to delete.
/// </summary>
/// <param name="client">The Durable Task client used to query the backend for tombstoned payloads.</param>
/// <param name="logger">The logger instance.</param>
[DurableTask]
public class GetTombstonedPayloadsActivity(
    DurableTaskClient client,
    ILogger<GetTombstonedPayloadsActivity> logger)
    : TaskActivity<int, List<TombstonedPayload>>
{
    readonly DurableTaskClient client = Check.NotNull(client);
    readonly ILogger<GetTombstonedPayloadsActivity> logger = Check.NotNull(logger);

    /// <inheritdoc/>
    public override async Task<List<TombstonedPayload>> RunAsync(TaskActivityContext context, int input)
    {
        int limit = input > 0 ? input : BlobPurgeConstants.DefaultBatchSize;
        List<TombstonedPayload> payloads =
            await this.client.GetTombstonedPayloadsAsync(limit, CancellationToken.None);
        this.logger.BlobPurgeFetchedTombstones(payloads.Count);
        return payloads;
    }
}
