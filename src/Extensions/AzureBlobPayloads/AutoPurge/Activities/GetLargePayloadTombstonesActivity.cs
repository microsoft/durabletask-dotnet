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

    /// <summary>
    /// Gets or sets the timeout for one backend RPC attempt.
    /// </summary>
    /// <remarks>
    /// This does not bound stop latency from activity scheduling, retries, or calls made by older workers.
    /// </remarks>
    internal TimeSpan RpcTimeout { get; set; } = TimeSpan.FromSeconds(BlobPurgeConstants.RpcTimeoutSeconds);

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
            using var call = this.client.GetLargePayloadTombstonesAsync(
                new LP.GetLargePayloadTombstonesRequest { Limit = input },
                deadline: DateTime.UtcNow.Add(this.RpcTimeout));
            response = await call;
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled)
        {
            throw new OperationCanceledException(
                "The GetLargePayloadTombstonesAsync operation was canceled.", e);
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.FailedPrecondition)
        {
            // The backend declined this fetch because a task-hub precondition is not met - auto-purge is
            // disabled, or the hub is being deleted. Return empty and mutate nothing durable: this observation
            // can race with a re-enable, so recording it would let a stale decline outlive the newer state.
            // The eternal runner idles and retries; the backend setting remains authoritative.
            this.logger.BlobPurgeFetchPreconditionFailed(e.Status.Detail);
            return new List<LargePayloadTombstone>();
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Unimplemented)
        {
            // Mixed-rollout guard: an older backend build (or a stale local emulator image) does not implement
            // this RPC. The orchestrator waits for an explicit enable event instead of retrying indefinitely.
            throw new NotImplementedException(
                "The Durable Task backend does not implement the GetLargePayloadTombstones RPC required for " +
                "large-payload auto-purge. The runner will wait for an explicit enable event. " +
                "Upgrade the backend (or re-pull " +
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
