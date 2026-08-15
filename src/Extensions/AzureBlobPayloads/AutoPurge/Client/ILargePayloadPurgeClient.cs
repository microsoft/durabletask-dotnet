// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Abstraction over the two backend RPCs the auto-purge job needs: fetching a batch of due large-payload
/// tombstones and reporting the outcome of each attempted blob deletion. The worker talks to the backend over
/// its own gRPC transport through this abstraction, so the purge activities never depend on a
/// <see cref="DurableTaskClient"/> - which a worker-only ("client triggers, worker executes") host would never
/// register.
/// </summary>
internal interface ILargePayloadPurgeClient
{
    /// <summary>
    /// Fetches a bounded batch of due large-payload tombstones from the backend.
    /// </summary>
    /// <param name="limit">The maximum number of tombstones to return. Must be in the range 1..1000.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>The due tombstones, up to <paramref name="limit"/> of them.</returns>
    Task<List<LargePayloadTombstone>> GetLargePayloadTombstonesAsync(
        int limit, CancellationToken cancellation = default);

    /// <summary>
    /// Reports the outcome of every attempted blob deletion to the backend so it can hard-delete the resolved
    /// rows, reschedule the retryable ones, and quarantine the rest.
    /// </summary>
    /// <param name="results">The per-tombstone purge results to report.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A task that completes once the backend has accepted the report.</returns>
    Task ReportLargePayloadPurgeResultsAsync(
        IEnumerable<LargePayloadPurgeResult> results, CancellationToken cancellation = default);
}
