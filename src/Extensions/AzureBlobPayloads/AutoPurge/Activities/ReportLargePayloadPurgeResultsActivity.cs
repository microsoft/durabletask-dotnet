// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Activity that reports the outcome of every attempted blob deletion to the backend, so it can delete the
/// resolved tombstones, reschedule the retryable ones, and quarantine the rest. Every attempted row is
/// reported, not only the successful ones: the backend owns retry scheduling, so a row it hears nothing about
/// would simply be re-served unchanged on the next cycle.
/// </summary>
/// <param name="client">The Durable Task client used to report purge results to the backend.</param>
/// <param name="logger">The logger instance.</param>
[DurableTask]
public class ReportLargePayloadPurgeResultsActivity(
    DurableTaskClient client,
    ILogger<ReportLargePayloadPurgeResultsActivity> logger)
    : TaskActivity<List<LargePayloadPurgeResult>, object?>
{
    readonly DurableTaskClient client = Check.NotNull(client);
    readonly ILogger<ReportLargePayloadPurgeResultsActivity> logger = Check.NotNull(logger);

    /// <inheritdoc/>
    public override async Task<object?> RunAsync(
        TaskActivityContext context, List<LargePayloadPurgeResult> input)
    {
        if (input is null || input.Count == 0)
        {
            return null;
        }

        await this.client.ReportLargePayloadPurgeResultsAsync(input, CancellationToken.None);
        this.logger.BlobPurgeReportedResults(input.Count);
        return null;
    }
}
