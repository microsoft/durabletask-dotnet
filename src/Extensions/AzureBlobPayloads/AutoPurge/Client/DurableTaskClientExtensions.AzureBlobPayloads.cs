// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Client.Grpc.Internal;
using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Extension methods that turn Azure Blob large-payload auto-purge on and off for a task hub.
/// </summary>
public static class DurableTaskClientExtensionsAzureBlobPayloads
{
    /// <summary>
    /// Turns large-payload blob auto-purge on or off for the task hub this client is authenticated against, and
    /// starts or stops the singleton purge job that does the deleting. Enabling also purges eligible old
    /// terminal runner instances, excluding the current activation.
    /// </summary>
    /// <param name="client">The Durable Task client. Must be the gRPC client, with entity support enabled.</param>
    /// <param name="enabled">
    /// <c>true</c> to tombstone externalized payloads on instance purge and run the job that deletes their
    /// blobs; <c>false</c> to stop tombstoning and stop the job.
    /// </param>
    /// <param name="batchSize">
    /// The maximum number of tombstones the job requests from the backend per cycle. Must be between 1 and
    /// <see cref="LargePayloadTombstone.MaxRequestLimit"/> inclusive. Ignored entirely when
    /// <paramref name="enabled"/> is <c>false</c>, including when out of range, because a job that is being
    /// stopped has no cycle to size.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes after the setting, entity signal, and enable-time cleanup have completed.</returns>
    /// <remarks>
    /// <para>
    /// One call owns one transition, and the caller owns when it happens. There is no host that applies this at
    /// startup and no loop that reasserts it: the setting and the job persist in the task hub, so a value set
    /// once outlives every process that was running when it was set. Repeating the same call is safe - the
    /// backend setting is last-writer-wins and the entity operations are idempotent - so a caller that is unsure
    /// of the current state can simply call again.
    /// </para>
    /// <para>
    /// This first performs two separate operations against the task hub: the backend setting is written
    /// first and awaited, then the singleton job entity is signalled. The order is deliberate on both paths.
    /// Enabling the setting before starting the job means the job cannot poll for tombstones the backend is not
    /// yet writing; disabling it before stopping the job means no new tombstones are created while the job winds
    /// down. There is no transaction across the two, and none is possible: they are different subsystems.
    /// </para>
    /// <para>
    /// After the enable signal is enqueued, cleanup snapshots all pages of completed, failed, or terminated
    /// generation-specific purge runners, reads the job entity state, and purges eligible old instances by
    /// exact ID without recursion. Metadata queries do not fetch orchestration inputs or outputs. The observed
    /// current generation is excluded even if its runner has completed, because Create can restart it. If
    /// Create is still queued, the previous generation is conservatively excluded. Running, pending, or
    /// suspended runners are not terminated; skipped runners may become eligible on a later enable.
    /// Disabling does not query or purge runner instances.
    /// </para>
    /// <para>
    /// Cleanup requires entity-state read and orchestration-management permissions in addition to the existing
    /// setting/signal permissions (for DTS custom roles: OrchestrationsReadAll and OrchestrationsManage).
    /// Query, entity-read, purge, and cancellation failures are surfaced after Create has been enqueued; the
    /// setting is not rolled back. This is not an atomic operation with another caller's enable or disable:
    /// callers must coordinate these transitions, including cleanup. Purge checkpoints use the backend's
    /// current auto-purge setting, so a concurrent disable can prevent references from becoming tombstones.
    /// </para>
    /// <para>
    /// Completion includes the acknowledged setting, reliably enqueued entity signal, and any enable-time
    /// cleanup, but does not mean the job has actually begun or ended. Starting is asynchronous: the
    /// entity schedules the perpetual orchestrator, which begins its first cycle shortly after. Stopping is
    /// cooperative: the orchestrator reads the job state at the top of each cycle and exits on its own, so
    /// tombstones already fetched and deletes already in flight run to completion first.
    /// </para>
    /// <para>
    /// Each activation gets a new generation-specific runner ID. Re-enabling need not wait for an older runner
    /// to finish: its in-flight cycle may overlap the new generation, but it exits when its next state read
    /// observes the change. Repeated enables while active retain the current generation and can resize its
    /// batches or restart a completed runner. This is one logical job per task hub, not a strict execution
    /// mutex; diagnostic progress counts may include duplicated work. Individual fetch/report RPCs and blob
    /// delete attempts are bounded, but activity/entity scheduling delays mean shutdown has no global
    /// wall-clock guarantee.
    /// </para>
    /// <para>
    /// If the setting succeeds and the entity signal then fails or is cancelled, the setting is NOT rolled back
    /// and this throws. Rolling back would be its own operation that can fail in turn, and it would be wrong as
    /// often as it was right - a concurrent caller may have set the value the rollback would undo. Retry the
    /// same call instead; it converges from any partial state.
    /// </para>
    /// <para>
    /// There is no coordination between callers: no caller lease or owner. Two clients calling with
    /// different values race, and the last write wins for the backend setting and, independently, for the job
    /// entity - so a sufficiently unlucky interleaving can leave the setting from one caller with the job state
    /// from the other. Deciding who calls this, and when, is the caller's responsibility.
    /// </para>
    /// <para>
    /// Auto-purge reclaims only blobs referenced by self-describing <c>blob:v2:</c> tokens. Payloads written by
    /// SDK versions that emitted legacy <c>blob:v1:</c> tokens are not reclaimed, because a v1 token identifies
    /// the container by name only and not the storage account, so the delete cannot be verified. Their backing
    /// blobs remain in storage exactly as they did before auto-purge existed, and the backend removes their rows
    /// normally. Note also that when the storage account has blob versioning or blob soft delete enabled,
    /// auto-purge deletes the current base blob, but retained versions and soft-deleted blobs keep consuming
    /// storage until a lifecycle-management policy or the retention period reclaims them.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="enabled"/> is <c>true</c> and <paramref name="batchSize"/> is out of range.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The client is not the gRPC client, or was built with entity support disabled.
    /// </exception>
    /// <exception cref="NotImplementedException">The backend does not implement large-payload purge.</exception>
    public static async Task SetLargePayloadAutoPurgeAsync(
        this DurableTaskClient client,
        bool enabled,
        int batchSize = BlobPurgeConstants.DefaultBatchSize,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(client);

        // Validated only on the enabling path. On the disabling path the value is not used at all - there is no
        // cycle left to size - so rejecting it would fail a call that would otherwise have done exactly what the
        // caller wanted, which matters most for the caller who is passing a batch size through from
        // configuration and flipping only the flag.
        if (enabled && (batchSize < 1 || batchSize > BlobPurgeConstants.MaxBatchSize))
        {
            throw new ArgumentOutOfRangeException(
                nameof(batchSize),
                batchSize,
                $"{nameof(batchSize)} must be between 1 and {BlobPurgeConstants.MaxBatchSize} (inclusive).");
        }

        // Both local prerequisites are resolved BEFORE the RPC, so a client that cannot complete the call fails
        // without having changed anything in the backend. Discovering the missing entity client afterwards would
        // leave the setting written and the job unreachable - the enabling path's worst outcome, since the
        // backend would start tombstoning payloads that nothing is running to delete.
        if (client is not ILargePayloadAutoPurgeClient autoPurgeClient)
        {
            throw new NotSupportedException(
                $"Large-payload auto-purge requires the gRPC Durable Task client, but this client is " +
                $"'{client.GetType().FullName}'.");
        }

        DurableEntityClient entities = client.Entities;

        await autoPurgeClient.SetLargePayloadAutoPurgeAsync(enabled, cancellationToken);

        EntityInstanceId entityId = new(nameof(BlobPurgeJob), BlobPurgeConstants.JobId);

        // Signalled directly rather than driven through an orchestration. The entity is the job's authority and
        // its operations are idempotent, so there is nothing a bridge orchestration would add here: the caller
        // is explicit and single, not a fleet of hosts racing to converge.
        if (enabled)
        {
            await entities.SignalEntityAsync(
                entityId, nameof(BlobPurgeJob.Create), batchSize, cancellation: cancellationToken);
            await PurgePreviousRunnersAsync(client, entities, entityId, cancellationToken);
        }
        else
        {
            // A stop aimed at an entity that was never created materializes it with default (Pending) state,
            // because the framework persists entity state after every operation. That is accepted rather than
            // avoided with a read-before-signal: the read would be a second round trip that can disagree with
            // the signal that follows it, and a Pending job is exactly the state an explicit disable wants.
            await entities.SignalEntityAsync(
                entityId, nameof(BlobPurgeJob.Stop), cancellation: cancellationToken);
        }
    }

    static async Task PurgePreviousRunnersAsync(
        DurableTaskClient client, DurableEntityClient entities, EntityInstanceId entityId, CancellationToken cancellation)
    {
        string prefix = BlobPurgeConstants.OrchestratorInstanceIdPrefix + BlobPurgeConstants.JobId + "-";
        OrchestrationQuery query = new(
            InstanceIdPrefix: prefix,
            Statuses: [OrchestrationRuntimeStatus.Completed, OrchestrationRuntimeStatus.Failed, OrchestrationRuntimeStatus.Terminated],
            PageSize: 100,
            FetchInputsAndOutputs: false);
        HashSet<string> candidates = new(StringComparer.Ordinal);
        await foreach (OrchestrationMetadata instance in client.GetAllInstancesAsync(query).WithCancellation(cancellation))
        {
            if (instance.IsCompleted && instance.Name == nameof(BlobPurgeJobOrchestrator)
                && instance.InstanceId.StartsWith(prefix, StringComparison.Ordinal)
                && instance.InstanceId.Length == prefix.Length + 32
                && Guid.TryParseExact(instance.InstanceId.Substring(prefix.Length), "N", out Guid generation)
                && instance.InstanceId.Substring(prefix.Length) == generation.ToString("N"))
            {
                candidates.Add(instance.InstanceId);
            }
        }

        if (candidates.Count == 0)
        {
            return;
        }

        // Read AFTER all candidate pages. A Create processed during enumeration is excluded here; a future
        // activation cannot reuse an ID from this snapshot. Do not mutate the collection while paging it.
        EntityMetadata<BlobPurgeJobState>? entity =
            await entities.GetEntityAsync<BlobPurgeJobState>(entityId, includeState: true, cancellation);
        string? currentId = null;
        if (entity is not null)
        {
            BlobPurgeJobState state = entity.State;
            if (state.Status == BlobPurgeJobStatus.Active)
            {
                Check.NotNullOrEmpty(state.Generation);
            }

            if (state.Generation is not null)
            {
                currentId = BlobPurgeConstants.GetOrchestratorInstanceId(BlobPurgeConstants.JobId, state.Generation);
            }
        }

        foreach (string instanceId in candidates)
        {
            cancellation.ThrowIfCancellationRequested();
            if (instanceId != currentId)
            {
                await client.PurgeInstanceAsync(instanceId, new PurgeInstanceOptions(Recursive: false), cancellation);
            }
        }
    }
}
