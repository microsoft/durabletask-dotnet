// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Activity that acknowledges to the backend the payloads whose blobs the worker has deleted, so the backend
/// can hard-delete the soft-deleted rows.
/// </summary>
/// <param name="client">The Durable Task client used to acknowledge purged payloads to the backend.</param>
/// <param name="logger">The logger instance.</param>
[DurableTask]
public class AckPurgedPayloadsActivity(
    DurableTaskClient client,
    ILogger<AckPurgedPayloadsActivity> logger)
    : TaskActivity<List<PayloadPurgeAck>, object?>
{
    readonly DurableTaskClient client = Check.NotNull(client);
    readonly ILogger<AckPurgedPayloadsActivity> logger = Check.NotNull(logger);

    /// <inheritdoc/>
    public override async Task<object?> RunAsync(TaskActivityContext context, List<PayloadPurgeAck> input)
    {
        if (input is null || input.Count == 0)
        {
            return null;
        }

        await this.client.AckPurgedPayloadsAsync(input, CancellationToken.None);
        this.logger.BlobPurgeAckedPayloads(input.Count);
        return null;
    }
}
