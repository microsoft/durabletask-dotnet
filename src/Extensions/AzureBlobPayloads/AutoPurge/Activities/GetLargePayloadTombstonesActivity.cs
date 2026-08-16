// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using static Microsoft.DurableTask.Protobuf.TaskHubSidecarService;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Activity that fetches a bounded batch of due large-payload tombstones from the backend for the auto-purge
/// job to delete.
/// </summary>
/// <param name="client">The sidecar service client used to query the backend for tombstones.</param>
/// <param name="logger">The logger instance.</param>
[DurableTask]
internal sealed class GetLargePayloadTombstonesActivity(
    TaskHubSidecarServiceClient client,
    ILogger<GetLargePayloadTombstonesActivity> logger)
    : TaskActivity<int, List<LargePayloadTombstone>>
{
    readonly TaskHubSidecarServiceClient client = Check.NotNull(client);
    readonly ILogger<GetLargePayloadTombstonesActivity> logger = Check.NotNull(logger);

    /// <inheritdoc/>
    public override async Task<List<LargePayloadTombstone>> RunAsync(TaskActivityContext context, int input)
    {
        if (input <= 0 || input > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input), input, "Limit must be greater than 0 and less than or equal to 1000.");
        }

        P.GetLargePayloadTombstonesResponse response;
        try
        {
            response = await this.client.GetLargePayloadTombstonesAsync(
                new P.GetLargePayloadTombstonesRequest { Limit = input });
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled)
        {
            throw new OperationCanceledException(
                "The GetLargePayloadTombstonesAsync operation was canceled.", e);
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Unimplemented)
        {
            // Mixed-rollout guard: an older backend build (or a stale local emulator image) does not implement
            // this RPC. Surfacing NotImplementedException lets the orchestrator disable the job instead of
            // retrying an operation that can never succeed - see BlobPurgeJobOrchestrator's handling of it.
            throw new NotImplementedException(
                "The Durable Task backend does not implement the GetLargePayloadTombstones RPC required for " +
                "large-payload auto-purge. Upgrade the backend (or re-pull " +
                "'mcr.microsoft.com/dts/dts-emulator'), then restart the app. Auto-purge is now disabled until " +
                $"the process restarts. Backend detail: {e.Status.Detail}",
                e);
        }

        List<LargePayloadTombstone> tombstones = new(response.Tombstones.Count);
        foreach (P.LargePayloadTombstone tombstone in response.Tombstones)
        {
            tombstones.Add(new LargePayloadTombstone(
                tombstone.PartitionId,
                tombstone.InstanceKey,
                tombstone.PayloadId,
                tombstone.Token,
                tombstone.Revision));
        }

        this.logger.BlobPurgeFetchedTombstones(tombstones.Count);
        return tombstones;
    }
}
