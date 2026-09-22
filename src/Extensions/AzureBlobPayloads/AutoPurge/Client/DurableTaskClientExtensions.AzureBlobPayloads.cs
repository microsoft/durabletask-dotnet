// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core.Exceptions;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Client.Grpc.Internal;

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Controls large-payload auto-purge for the authenticated task hub.
/// </summary>
public static class DurableTaskClientExtensionsAzureBlobPayloads
{
    /// <summary>
    /// Enables or pauses large-payload auto-purge. Enabling ensures the hub's fixed purge orchestration has
    /// started and reliably enqueues its batch-size configuration.
    /// </summary>
    /// <param name="client">A Durable Task gRPC client.</param>
    /// <param name="enabled">True to enable cleanup; false to pause new fetches through the backend setting.</param>
    /// <param name="batchSize">The requested batch size, from 1 through 1000. Ignored when disabling.</param>
    /// <param name="cancellationToken">Cancels any setting, start, wait or configuration operation.</param>
    /// <returns>A task that completes after the backend setting and, when enabling, start verification and event enqueue.</returns>
    /// <remarks>
    /// <para>
    /// The backend setting is authoritative. Disabling only writes that setting: it does not create, terminate,
    /// suspend or wait for an orchestration. Already-fetched work may finish. An existing purge orchestration
    /// remains Running while logically paused and polls on a one-minute durable timer; disabled fetches return
    /// an empty batch. An unsupported backend instead waits for an explicit enable configuration event.
    /// </para>
    /// <para>
    /// Enabling first writes the setting, then schedules the fixed-ID orchestration and lets the backend
    /// deduplicate the start without replacing a running, pending, suspended or transitioning instance.
    /// Completed, failed, canceled or terminated instances can be replaced. This ID is reserved exclusively
    /// for the SDK's purge runner and must not be used by application code. The start wait must return the
    /// expected Running orchestration before configuration is enqueued; a suspended instance or live ID
    /// collision is an error.
    /// Existing input is not overwritten; repeated enables update batch size through an idempotent SetBatchSize
    /// event. Completion acknowledges that event, not that the runner has already applied it.
    /// New purge runners are explicitly unversioned and do not inherit the client's business default version.
    /// Enabling does not change an existing runner's version.
    /// </para>
    /// <para>
    /// These steps are not a transaction. Failures and cancellation propagate without rolling back earlier
    /// steps. Callers must coordinate enable/disable and management operations; concurrent termination, purge
    /// or suspension can invalidate an observed start. The caller needs metadata-read, start and raise-event
    /// permissions as well as permission to change the backend setting. Entity support is not required.
    /// </para>
    /// <para>
    /// Auto-purge reclaims only self-describing blob:v2 payload references; legacy blob:v1 payloads remain in
    /// storage. Blob versions, soft-deleted data and retention policies can continue consuming storage after
    /// a successful delete. Diagnostic progress is not a distinct physical-blob count.
    /// </para>
    /// </remarks>
    public static async Task SetLargePayloadAutoPurgeAsync(
        this DurableTaskClient client,
        bool enabled,
        int batchSize = BlobPurgeConstants.DefaultBatchSize,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(client);
        if (enabled && (batchSize < 1 || batchSize > BlobPurgeConstants.MaxBatchSize))
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Purge batch size is out of range.");
        }

        if (client is not ILargePayloadAutoPurgeClient autoPurgeClient)
        {
            throw new NotSupportedException($"Large-payload auto-purge requires a gRPC Durable Task client, not '{client.GetType().FullName}'.");
        }

        await SetLargePayloadAutoPurgeCoreAsync(
            client, autoPurgeClient.SetLargePayloadAutoPurgeAsync, enabled, batchSize, cancellationToken);
    }

    /// <summary>
    /// Applies explicit auto-purge configuration using an alternate host's task-hub-bound clients.
    /// </summary>
    /// <param name="client">The orchestration client targeting the same authenticated task hub as <paramref name="purgeClient"/>.</param>
    /// <param name="purgeClient">The host-owned transport for the task hub's auto-purge setting.</param>
    /// <param name="enabled">True to enable cleanup; false to pause new fetches through the backend setting.</param>
    /// <param name="batchSize">The requested batch size, from 1 through 1000. Ignored when disabling.</param>
    /// <param name="cancellationToken">Cancels any setting, start, wait or configuration operation.</param>
    /// <returns>A task that completes after the setting and, when enabling, start verification and event enqueue.</returns>
    /// <remarks>
    /// Infrastructure integration API, not an application orchestration API. The caller must bind both clients
    /// to the same task hub; neither client is disposed by this method. This uses the same nontransactional
    /// setting, fixed-ID deduplicated start, Running identity verification and batch-size event sequence as
    /// the standalone overload. Disabling only writes the setting. Repeating desired state after host takeover
    /// is supported; this does not implement owner election or guarantee once-ever setup.
    /// The host must register the SDK purge tasks before enabling and retain their canonical names and inputs.
    /// </remarks>
    public static async Task SetLargePayloadAutoPurgeAsync(
        this DurableTaskClient client,
        ILargePayloadPurgeClient purgeClient,
        bool enabled,
        int batchSize = BlobPurgeConstants.DefaultBatchSize,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(client);
        Check.NotNull(purgeClient);
        if (enabled && (batchSize < 1 || batchSize > BlobPurgeConstants.MaxBatchSize))
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Purge batch size is out of range.");
        }

        await SetLargePayloadAutoPurgeCoreAsync(
            client, purgeClient.SetLargePayloadAutoPurgeAsync, enabled, batchSize, cancellationToken);
    }

    static async Task SetLargePayloadAutoPurgeCoreAsync(
        DurableTaskClient client,
        Func<bool, CancellationToken, Task> setEnabled,
        bool enabled,
        int batchSize,
        CancellationToken cancellationToken)
    {
        await setEnabled(enabled, cancellationToken);
        if (!enabled)
        {
            return;
        }

        StartOrchestrationOptions options = new(BlobPurgeConstants.OrchestratorInstanceId)
        {
            Version = string.Empty,

            // Dedupe is the inverse of the wire's replaceable statuses. Never replace nonterminal work.
            DedupeStatuses = ["Running", "Pending", "Suspended", "ContinuedAsNew"],
        };
        try
        {
            string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
                nameof(BlobPurgeJobOrchestrator), new BlobPurgeJobRunRequest(batchSize), options, cancellationToken);
            if (instanceId != BlobPurgeConstants.OrchestratorInstanceId)
            {
                throw new InvalidOperationException("The backend returned an unexpected auto-purge instance ID.");
            }
        }
        catch (OrchestrationAlreadyExistsException)
        {
            // The backend retained an existing runner. Its identity and status still require verification.
        }

        OrchestrationMetadata started = await client.WaitForInstanceStartAsync(
            BlobPurgeConstants.OrchestratorInstanceId, getInputsAndOutputs: false, cancellationToken);
        if (started.InstanceId != BlobPurgeConstants.OrchestratorInstanceId || started.Name != nameof(BlobPurgeJobOrchestrator))
        {
            throw new InvalidOperationException("The reserved auto-purge instance ID belongs to a different orchestration.");
        }

        if (started.RuntimeStatus != OrchestrationRuntimeStatus.Running)
        {
            throw new InvalidOperationException($"Auto-purge did not start: its runtime status is {started.RuntimeStatus}.");
        }

        await client.RaiseEventAsync(
            BlobPurgeConstants.OrchestratorInstanceId, BlobPurgeConstants.SetBatchSizeEvent, batchSize, cancellationToken);
    }
}
