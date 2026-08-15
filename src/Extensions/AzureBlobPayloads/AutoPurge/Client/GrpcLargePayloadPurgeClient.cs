// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.Extensions.Options;
using static Microsoft.DurableTask.Protobuf.TaskHubSidecarService;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// gRPC implementation of <see cref="ILargePayloadPurgeClient"/> that reuses the worker's own transport. It
/// builds a <see cref="TaskHubSidecarServiceClient"/> from the named <see cref="GrpcDurableTaskWorkerOptions"/>,
/// so the two purge RPCs travel the exact same channel the worker already uses to reach the backend, with no
/// dependency on a client-side <see cref="DurableTaskClient"/>. The marshalling mirrors
/// <c>GrpcDurableTaskClient</c>'s equivalent overrides so the wire behavior is identical either way.
/// </summary>
internal sealed class GrpcLargePayloadPurgeClient : ILargePayloadPurgeClient
{
    readonly IOptionsMonitor<GrpcDurableTaskWorkerOptions> options;
    readonly string name;
    readonly object gate = new();
    TaskHubSidecarServiceClient? sidecarClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcLargePayloadPurgeClient"/> class.
    /// </summary>
    /// <param name="options">The monitor used to resolve the worker's gRPC options by builder name.</param>
    /// <param name="name">The builder name whose worker options carry the transport to reuse.</param>
    public GrpcLargePayloadPurgeClient(IOptionsMonitor<GrpcDurableTaskWorkerOptions> options, string name)
    {
        this.options = Check.NotNull(options);
        this.name = name ?? string.Empty;
    }

    /// <inheritdoc/>
    public async Task<List<LargePayloadTombstone>> GetLargePayloadTombstonesAsync(
        int limit, CancellationToken cancellation = default)
    {
        if (limit <= 0 || limit > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit), limit, "Limit must be greater than 0 and less than or equal to 1000.");
        }

        TaskHubSidecarServiceClient client = this.GetClient();

        P.GetLargePayloadTombstonesResponse response;
        try
        {
            response = await client.GetLargePayloadTombstonesAsync(
                new P.GetLargePayloadTombstonesRequest { Limit = limit },
                cancellationToken: cancellation);
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled)
        {
            throw new OperationCanceledException(
                $"The {nameof(this.GetLargePayloadTombstonesAsync)} operation was canceled.", e, cancellation);
        }

        List<LargePayloadTombstone> result = new(response.Tombstones.Count);
        foreach (P.LargePayloadTombstone tombstone in response.Tombstones)
        {
            result.Add(new LargePayloadTombstone(
                tombstone.PartitionId,
                tombstone.InstanceKey,
                tombstone.PayloadId,
                tombstone.Token,
                tombstone.Revision));
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task ReportLargePayloadPurgeResultsAsync(
        IEnumerable<LargePayloadPurgeResult> results, CancellationToken cancellation = default)
    {
        Check.NotNull(results);

        P.ReportLargePayloadPurgeResultsRequest request = new();
        foreach (LargePayloadPurgeResult result in results)
        {
            request.Results.Add(new P.LargePayloadPurgeResult
            {
                PartitionId = result.PartitionId,
                InstanceKey = result.InstanceKey,
                PayloadId = result.PayloadId,
                Revision = result.Revision,

                // The managed disposition enum declares the same numeric values as its protobuf counterpart,
                // so it maps across by value. This is the only enum on the message and it only travels
                // outbound, so the SDK can never receive a value it does not know.
                Disposition = (P.LargePayloadPurgeDisposition)result.Disposition,
            });
        }

        if (request.Results.Count == 0)
        {
            return;
        }

        TaskHubSidecarServiceClient client = this.GetClient();
        try
        {
            await client.ReportLargePayloadPurgeResultsAsync(request, cancellationToken: cancellation);
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled)
        {
            throw new OperationCanceledException(
                $"The {nameof(this.ReportLargePayloadPurgeResultsAsync)} operation was canceled.", e, cancellation);
        }
    }

    TaskHubSidecarServiceClient GetClient()
    {
        // Resolve the transport lazily and cache it. The worker's Channel/CallInvoker is only guaranteed to be
        // populated once the options have been fully post-configured (the AzureBlobPayloads worker setup moves
        // the Channel onto CallInvoker, or throws if neither was set), which happens after this service is
        // constructed. Building on first RPC keeps the purge activities constructible in hosts that never run
        // the job - which is exactly the worker-only case this abstraction exists to unblock.
        lock (this.gate)
        {
            if (this.sidecarClient is not null)
            {
                return this.sidecarClient;
            }

            GrpcDurableTaskWorkerOptions opts = this.options.Get(this.name);

            // Mirror the worker's own Channel -> CallInvoker precedence (GrpcDurableTaskWorker.GetCallInvoker):
            // reuse whichever the worker resolved so the purge RPCs share its exact transport. The worker also
            // has an Address-only branch that builds an owned channel, but it is unreachable here - the
            // AzureBlobPayloads worker PostConfigure guarantees one of these two is set or throws first - so we
            // do not duplicate its per-target-framework channel-lifetime logic.
            CallInvoker callInvoker;
            if (opts.Channel is { } channel)
            {
                callInvoker = channel.CreateCallInvoker();
            }
            else if (opts.CallInvoker is { } invoker)
            {
                callInvoker = invoker;
            }
            else
            {
                throw new InvalidOperationException(
                    "A gRPC Channel or CallInvoker must be configured on the worker to purge externalized payloads.");
            }

            this.sidecarClient = new TaskHubSidecarServiceClient(callInvoker);
            return this.sidecarClient;
        }
    }
}
