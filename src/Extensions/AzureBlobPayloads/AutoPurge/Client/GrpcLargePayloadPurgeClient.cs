// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using static Microsoft.DurableTask.Protobuf.LargePayloads.LargePayloadPurge;
using LP = Microsoft.DurableTask.Protobuf.LargePayloads;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Adapts the worker's existing, rebindable purge transport without owning its lifetime.
/// </summary>
sealed class GrpcLargePayloadPurgeClient(LargePayloadPurgeClient client) : ILargePayloadPurgeClient
{
    readonly LargePayloadPurgeClient client = Check.NotNull(client);

    /// <inheritdoc/>
    public async Task<List<LargePayloadTombstone>> GetLargePayloadTombstonesAsync(
        int limit, DateTime deadline, CancellationToken cancellationToken = default)
    {
        using var call = this.client.GetLargePayloadTombstonesAsync(
            new LP.GetLargePayloadTombstonesRequest { Limit = limit }, deadline: deadline, cancellationToken: cancellationToken);
        LP.GetLargePayloadTombstonesResponse response = await call;
        List<LargePayloadTombstone> tombstones = new(response.Tombstones.Count);
        foreach (LP.LargePayloadTombstone tombstone in response.Tombstones)
        {
            tombstones.Add(new LargePayloadTombstone(tombstone.TombstoneToken, tombstone.PayloadToken));
        }

        return tombstones;
    }

    /// <inheritdoc/>
    public async Task ReportLargePayloadPurgeResultsAsync(
        IReadOnlyList<LargePayloadPurgeResult> results, DateTime deadline, CancellationToken cancellationToken = default)
    {
        LP.ReportLargePayloadPurgeResultsRequest request = new();
        foreach (LargePayloadPurgeResult result in results)
        {
            request.Results.Add(new LP.LargePayloadPurgeResult
            {
                // Echo the opaque correlation token unchanged. The managed and protobuf enums share values.
                TombstoneToken = result.TombstoneToken,
                Disposition = (LP.LargePayloadPurgeDisposition)result.Disposition,
            });
        }

        using var call = this.client.ReportLargePayloadPurgeResultsAsync(
            request, deadline: deadline, cancellationToken: cancellationToken);
        await call;
    }
}
