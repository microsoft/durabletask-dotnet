// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Provides task-hub-bound transport operations for integrating blob auto-purge with an alternate .NET host.
/// </summary>
/// <remarks>
/// This is an infrastructure integration API, not an application orchestration API. Implementations must
/// use the same authenticated task hub as the associated orchestration client and preserve its authentication,
/// metadata, reconnection and transport lifetime. The SDK does not own or dispose the supplied client.
/// Fetch and report must propagate gRPC status exceptions unchanged: the activities own their cancellation,
/// unsupported-backend and fetch-precondition handling. They must honor the supplied UTC deadline.
/// Correlation tokens must be returned and reported exactly as received, without parsing or reconstruction.
/// </remarks>
public interface ILargePayloadPurgeClient
{
    /// <summary>
    /// Fetches a bounded batch of due tombstones for this client's authenticated task hub.
    /// </summary>
    /// <param name="limit">The requested maximum number of tombstones, from 1 through 1000.</param>
    /// <param name="deadline">The absolute UTC deadline for this backend attempt.</param>
    /// <param name="cancellationToken">Cancels the fetch operation.</param>
    /// <returns>The fetched tombstones, with their opaque correlation and payload tokens unchanged.</returns>
    Task<List<LargePayloadTombstone>> GetLargePayloadTombstonesAsync(
        int limit, DateTime deadline, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports deletion outcomes for this client's authenticated task hub.
    /// </summary>
    /// <param name="results">The outcomes, each carrying the exact correlation token received during fetch.</param>
    /// <param name="deadline">The absolute UTC deadline for this backend attempt.</param>
    /// <param name="cancellationToken">Cancels the report operation.</param>
    /// <returns>A task that completes when the backend acknowledges the results.</returns>
    Task ReportLargePayloadPurgeResultsAsync(
        IReadOnlyList<LargePayloadPurgeResult> results, DateTime deadline, CancellationToken cancellationToken = default);
}
