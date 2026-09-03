// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using static Microsoft.DurableTask.Protobuf.LargePayloads.LargePayloadPurge;
using LP = Microsoft.DurableTask.Protobuf.LargePayloads;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Activity that fetches a bounded batch of due large-payload tombstones from the backend for the auto-purge
/// job to delete.
/// </summary>
/// <param name="client">The large-payload purge service client used to query the backend for tombstones.</param>
/// <param name="logger">The logger instance.</param>
[DurableTask]
internal sealed class GetLargePayloadTombstonesActivity(
    LargePayloadPurgeClient client,
    ILogger<GetLargePayloadTombstonesActivity> logger)
    : TaskActivity<int, List<LargePayloadTombstone>>
{
    readonly LargePayloadPurgeClient client = Check.NotNull(client);
    readonly ILogger<GetLargePayloadTombstonesActivity> logger = Check.NotNull(logger);

    /// <inheritdoc/>
    public override async Task<List<LargePayloadTombstone>> RunAsync(TaskActivityContext context, int input)
    {
        if (input <= 0 || input > LargePayloadTombstone.MaxRequestLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input), input, $"Limit must be greater than 0 and less than or equal to {LargePayloadTombstone.MaxRequestLimit}.");
        }

        LP.GetLargePayloadTombstonesResponse response;
        try
        {
            response = await this.client.GetLargePayloadTombstonesAsync(
                new LP.GetLargePayloadTombstonesRequest { Limit = input });
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled)
        {
            throw new OperationCanceledException(
                "The GetLargePayloadTombstonesAsync operation was canceled.", e);
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.FailedPrecondition)
        {
            // The backend declined this fetch because a task-hub precondition is not met - auto-purge is
            // disabled, or the hub is being deleted. Returning empty instead of throwing is deliberate: it
            // costs one idle cycle and nothing else, and it leaves the job running so the next cycle re-reads
            // the entity and re-asks the backend. Throwing, or disabling the job here, would let a decline
            // observed mid-disable durably defeat a re-enable that lands moments later. A real disable's Stop
            // signal is what ends the loop, on the next entity check.
            this.logger.BlobPurgeFetchPreconditionFailed(e.Status.Detail);
            return new List<LargePayloadTombstone>();
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Unimplemented)
        {
            // Mixed-rollout guard: an older backend build (or a stale local emulator image) does not implement
            // this RPC. Surfacing NotImplementedException lets the orchestrator disable the job instead of
            // retrying an operation that can never succeed - see BlobPurgeJobOrchestrator's handling of it.
            throw new NotImplementedException(
                "The Durable Task backend does not implement the GetLargePayloadTombstones RPC required for " +
                "large-payload auto-purge. Auto-purge is now disabled. Upgrade the backend (or re-pull " +
                "'mcr.microsoft.com/dts/dts-emulator'), then call SetLargePayloadAutoPurgeAsync(true, ...) " +
                $"again to re-enable it. Backend detail: {e.Status.Detail}",
                e);
        }

        List<LargePayloadTombstone> tombstones = new(response.Tombstones.Count);
        foreach (LP.LargePayloadTombstone tombstone in response.Tombstones)
        {
            tombstones.Add(new LargePayloadTombstone(tombstone.TombstoneToken, tombstone.PayloadToken));
        }

        this.logger.BlobPurgeFetchedTombstones(tombstones.Count);
        return tombstones;
    }
}
