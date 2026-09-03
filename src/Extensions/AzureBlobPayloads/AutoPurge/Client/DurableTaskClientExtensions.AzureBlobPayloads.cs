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
    /// starts or stops the singleton purge job that does the deleting.
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
    /// <returns>A task that completes once both steps below have been performed.</returns>
    /// <remarks>
    /// <para>
    /// One call owns one transition, and the caller owns when it happens. There is no host that applies this at
    /// startup and no loop that reasserts it: the setting and the job persist in the task hub, so a value set
    /// once outlives every process that was running when it was set. Repeating the same call is safe - the
    /// backend setting is last-writer-wins and the entity operations are idempotent - so a caller that is unsure
    /// of the current state can simply call again.
    /// </para>
    /// <para>
    /// This performs TWO separate operations against the task hub, in this order: the backend setting is written
    /// first and awaited, then the singleton job entity is signalled. The order is deliberate on both paths.
    /// Enabling the setting before starting the job means the job cannot poll for tombstones the backend is not
    /// yet writing; disabling it before stopping the job means no new tombstones are created while the job winds
    /// down. There is no transaction across the two, and none is possible: they are different subsystems.
    /// </para>
    /// <para>
    /// Because they are separate, completion means the backend acknowledged the setting and the entity signal
    /// was reliably enqueued - not that the job has actually begun or ended. Starting is asynchronous: the
    /// entity schedules the perpetual orchestrator, which begins its first cycle shortly after. Stopping is
    /// cooperative: the orchestrator reads the job state at the top of each cycle and exits on its own, so
    /// tombstones already fetched and deletes already in flight run to completion first.
    /// </para>
    /// <para>
    /// If the setting succeeds and the entity signal then fails or is cancelled, the setting is NOT rolled back
    /// and this throws. Rolling back would be its own operation that can fail in turn, and it would be wrong as
    /// often as it was right - a concurrent caller may have set the value the rollback would undo. Retry the
    /// same call instead; it converges from any partial state.
    /// </para>
    /// <para>
    /// There is no coordination between callers: no lease, no owner, no fencing. Two clients calling with
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
}
