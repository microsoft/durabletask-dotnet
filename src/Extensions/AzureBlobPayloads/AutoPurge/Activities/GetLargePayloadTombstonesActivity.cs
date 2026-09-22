// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using static Microsoft.DurableTask.Protobuf.LargePayloads.LargePayloadPurge;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Activity that fetches a bounded batch of due large-payload tombstones from the backend for the auto-purge
/// job to delete.
/// </summary>
/// <param name="client">The large-payload purge service client used to query the backend for tombstones.</param>
/// <param name="logger">The logger instance.</param>
/// <remarks>
/// Infrastructure integration API for alternate .NET hosts. The supplied client must be bound to this
/// worker's authenticated task hub. Its transport lifetime remains owned by the host.
/// </remarks>
[DurableTask]
public sealed class GetLargePayloadTombstonesActivity(
    ILargePayloadPurgeClient client,
    ILogger<GetLargePayloadTombstonesActivity> logger)
    : TaskActivity<int, List<LargePayloadTombstone>>
{
    readonly ILargePayloadPurgeClient client = Check.NotNull(client);
    readonly ILogger<GetLargePayloadTombstonesActivity> logger = Check.NotNull(logger);

    /// <summary>
    /// Initializes a new instance of the <see cref="GetLargePayloadTombstonesActivity"/> class using the worker's transport.
    /// </summary>
    /// <param name="client">The worker's purge client.</param>
    /// <param name="logger">The activity logger.</param>
    internal GetLargePayloadTombstonesActivity(
        LargePayloadPurgeClient client, ILogger<GetLargePayloadTombstonesActivity> logger)
        : this(new GrpcLargePayloadPurgeClient(client), logger)
    {
    }

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

        List<LargePayloadTombstone> tombstones;
        try
        {
            tombstones = await this.client.GetLargePayloadTombstonesAsync(input, DateTime.UtcNow.Add(this.RpcTimeout));
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

        this.logger.BlobPurgeFetchedTombstones(tombstones.Count);
        return tombstones;
    }
}
