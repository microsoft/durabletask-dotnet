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

        await autoPurgeClient.SetLargePayloadAutoPurgeAsync(enabled, cancellationToken);
        if (!enabled)
        {
            return;
        }

        StartOrchestrationOptions options = new(BlobPurgeConstants.OrchestratorInstanceId)
        {
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
