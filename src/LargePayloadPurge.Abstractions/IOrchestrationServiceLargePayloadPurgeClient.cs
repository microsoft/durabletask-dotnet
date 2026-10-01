// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.AzureBlobPayloads;

namespace DurableTask.LargePayloadPurge;

/// <summary>
/// Optional orchestration service client capability for purging tombstoned large payloads.
/// </summary>
/// <remarks>
/// Extends the shared fetch and report transport contract with control of the task hub's auto-purge setting.
/// </remarks>
public interface IOrchestrationServiceLargePayloadPurgeClient : ILargePayloadPurgeClient
{
    /// <summary>
    /// Records whether large payload auto-purge is enabled for the client's task hub.
    /// </summary>
    /// <remarks>This operation does not start, stop, or wait for a purge runner.</remarks>
    /// <param name="enabled">Whether large payload auto-purge is enabled.</param>
    /// <param name="deadlineUtc">The caller's operation deadline in UTC, or <see cref="DateTime.MaxValue"/>
    /// when the caller has not specified a deadline.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task that represents the operation.</returns>
    Task SetLargePayloadAutoPurgeAsync(bool enabled, DateTime deadlineUtc, CancellationToken cancellationToken);
}
