// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using static Microsoft.DurableTask.Protobuf.LargePayloads.LargePayloadPurge;
using LP = Microsoft.DurableTask.Protobuf.LargePayloads;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Activity that reports the outcome of every attempted blob deletion to the backend, so it can delete the
/// resolved tombstones, reschedule the retryable ones, and quarantine the rest. Every attempted row is
/// reported, not only the successful ones: the backend owns retry scheduling, so a row it hears nothing about
/// would simply be re-served unchanged on the next cycle.
/// </summary>
/// <param name="client">The large-payload purge service client used to report purge results to the backend.</param>
/// <param name="logger">The logger instance.</param>
[DurableTask]
internal sealed class ReportLargePayloadPurgeResultsActivity(
    LargePayloadPurgeClient client,
    ILogger<ReportLargePayloadPurgeResultsActivity> logger)
    : TaskActivity<List<LargePayloadPurgeResult>, object?>
{
    readonly LargePayloadPurgeClient client = Check.NotNull(client);
    readonly ILogger<ReportLargePayloadPurgeResultsActivity> logger = Check.NotNull(logger);

    /// <inheritdoc/>
    public override async Task<object?> RunAsync(
        TaskActivityContext context, List<LargePayloadPurgeResult> input)
    {
        if (input is null || input.Count == 0)
        {
            return null;
        }

        LP.ReportLargePayloadPurgeResultsRequest request = new();
        foreach (LargePayloadPurgeResult result in input)
        {
            request.Results.Add(new LP.LargePayloadPurgeResult
            {
                // Echoed back exactly as it was received. The SDK never parses or rebuilds this token, so a
                // change to what the backend puts in it needs no change here.
                TombstoneToken = result.TombstoneToken,

                // The managed disposition enum declares the same numeric values as its protobuf counterpart,
                // so it maps across by value. This is the only enum on the message and it only travels
                // outbound, so the SDK can never receive a value it does not know.
                Disposition = (LP.LargePayloadPurgeDisposition)result.Disposition,
            });
        }

        if (request.Results.Count == 0)
        {
            return null;
        }

        try
        {
            await this.client.ReportLargePayloadPurgeResultsAsync(request);
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled)
        {
            throw new OperationCanceledException(
                "The ReportLargePayloadPurgeResultsAsync operation was canceled.", e);
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Unimplemented)
        {
            // Mixed-rollout guard: an older backend build (or a stale local emulator image) does not implement
            // this RPC. Surfacing NotImplementedException lets the orchestrator disable the job instead of
            // retrying an operation that can never succeed - see BlobPurgeJobOrchestrator's handling of it.
            throw new NotImplementedException(
                "The Durable Task backend does not implement the ReportLargePayloadPurgeResults RPC required " +
                "for large-payload auto-purge. Auto-purge is now disabled. Upgrade the backend (or re-pull " +
                "'mcr.microsoft.com/dts/dts-emulator'), then call SetLargePayloadAutoPurgeAsync(true, ...) " +
                $"again to re-enable it. Backend detail: {e.Status.Detail}",
                e);
        }

        this.logger.BlobPurgeReportedResults(input.Count);
        return null;
    }
}
