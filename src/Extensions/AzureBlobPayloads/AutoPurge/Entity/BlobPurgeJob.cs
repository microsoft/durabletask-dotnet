// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.AzureBlobPayloads;

/// <summary>
/// Durable entity that manages the lifecycle of the singleton blob payload auto-purge job.
/// </summary>
/// <param name="logger">The logger instance.</param>
class BlobPurgeJob(ILogger<BlobPurgeJob> logger) : TaskEntity<BlobPurgeJobState>
{
    /// <summary>
    /// Creates the auto-purge job, and starts its orchestrator if one is not already running. Because the job is
    /// a per-task-hub singleton, callers racing to create it converge on the same result rather than disturbing
    /// a running job. It also takes the batch size when the job is already
    /// <see cref="BlobPurgeJobStatus.Active"/>, which is what lets a later call resize a running job.
    /// </summary>
    /// <remarks>
    /// There is deliberately no guard against reviving an <see cref="BlobPurgeJobStatus.Unsupported"/> job.
    /// Create is only ever reached from an explicit client call, so reviving is precisely what the caller asked
    /// for - and it is the documented recovery once the backend has been upgraded to implement the purge RPCs.
    /// A guard here would make that recovery impossible without a state reset the API does not expose. If the
    /// backend is still unsupported, the orchestrator discovers it on the next cycle and marks the job again.
    /// </remarks>
    /// <param name="context">The entity context.</param>
    /// <param name="purgeBatchSize">
    /// The maximum number of tombstoned payloads to request from the backend per cycle.
    /// </param>
    public void Create(TaskEntityContext context, int purgeBatchSize)
    {
        if (this.State.Status == BlobPurgeJobStatus.Active)
        {
            // The batch size is taken because this is the only path by which a changed size reaches an active
            // job - the orchestrator re-reads it from here every cycle. Without this the value written by the
            // very first Create would be the only one the job ever used, and a batch size the backend rejects
            // would wedge it permanently.
            //
            // Written only when the value actually differs, which is what keeps LastModifiedAt tracking real
            // changes rather than the time of the last call. An entity written by a build that predates this
            // field carries zero, which differs from any real size, so the first Create after an upgrade still
            // repairs it.
            if (this.State.PurgeBatchSize != purgeBatchSize)
            {
                this.State.PurgeBatchSize = purgeBatchSize;
                this.State.LastModifiedAt = DateTimeOffset.UtcNow;
            }

            logger.BlobPurgeJobAlreadyRunning(context.Id.Key);

            // Run is re-signalled even though the job is already active, and this is what lets a repeated call
            // heal a job whose orchestrator has died. The signal is deliberately blind. An entity-initiated
            // start carries no reuse policy, so the backend decides its fate: it discards the start while the
            // target instance exists in any non-completed status, and purges and replaces it once the instance
            // has completed, terminated, failed or been canceled. A healthy orchestrator is therefore left
            // strictly alone and only a dead one is replaced.
            //
            // Being blind is the point, not a shortcut. The backend reaches that decision atomically, so
            // delegating it removes the race entirely. Checking the orchestrator's status here and signalling
            // only when it looked dead would reintroduce the window in which it dies - or recovers - between
            // the read and the signal, which is strictly worse than not asking.
            context.SignalEntity(context.Id, nameof(this.Run));
            return;
        }

        this.State.Status = BlobPurgeJobStatus.Active;
        this.State.PurgeBatchSize = purgeBatchSize;
        this.State.CreatedAt ??= DateTimeOffset.UtcNow;
        this.State.LastModifiedAt = DateTimeOffset.UtcNow;
        this.State.LastError = null;

        logger.BlobPurgeJobCreated(context.Id.Key);

        // Signal Run to start the perpetual purge orchestrator.
        context.SignalEntity(context.Id, nameof(this.Run));
    }

    /// <summary>
    /// Starts the purge orchestrator if the job is active.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The orchestrator runs under a fixed instance ID, which is what keeps the singleton a singleton. No reuse
    /// policy is passed, and none can be: the entity's start action has no field to carry one, so anything set
    /// here would be dropped before it reached the wire. That default is the behaviour the job relies on rather
    /// than an omission - the backend discards a start aimed at an instance that already exists in a
    /// non-completed status, and replaces the instance only once it has completed, terminated, failed or been
    /// canceled. Signalling this operation is therefore always safe, whatever the orchestrator is doing.
    /// </para>
    /// <para>
    /// This operation deliberately writes no state. It schedules an orchestrator and nothing more, so touching
    /// <see cref="BlobPurgeJobState.LastModifiedAt"/> here would overwrite a real change with the time of a
    /// call that changed nothing.
    /// </para>
    /// </remarks>
    /// <param name="context">The entity context.</param>
    public void Run(TaskEntityContext context)
    {
        if (this.State.Status != BlobPurgeJobStatus.Active)
        {
            return;
        }

        string instanceId = BlobPurgeConstants.GetOrchestratorInstanceId(context.Id.Key);
        StartOrchestrationOptions startOrchestrationOptions = new(instanceId);

        context.ScheduleNewOrchestration(
            new TaskName(nameof(BlobPurgeJobOrchestrator)),
            new BlobPurgeJobRunRequest(context.Id, this.State.PurgeBatchSize),
            startOrchestrationOptions);
    }

    /// <summary>
    /// Stops the auto-purge job.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The perpetual orchestrator is deliberately not terminated here, and this operation does not touch its
    /// instance ID at all. The orchestrator reads this entity at the top of every cycle and exits on its own
    /// once the job is no longer <see cref="BlobPurgeJobStatus.Active"/>, so shutdown is cooperative: there is
    /// no window in which one party terminates an orchestrator that the other believes is healthy, and the
    /// in-flight cycle finishes rather than being cut off part-way through a batch of deletes.
    /// </para>
    /// <para>
    /// <see cref="BlobPurgeJobState.CreatedAt"/>, <see cref="BlobPurgeJobState.PurgedCount"/> and
    /// <see cref="BlobPurgeJobState.PurgeBatchSize"/> are preserved. They are the job's history and its
    /// configuration, both of which are wanted if it is started again, and keeping <c>CreatedAt</c> is also what
    /// distinguishes a stopped job from one that was never started.
    /// </para>
    /// </remarks>
    /// <param name="context">The entity context.</param>
    public void Stop(TaskEntityContext context)
    {
        if (this.State.Status != BlobPurgeJobStatus.Active)
        {
            // Load-bearing. Stop is signalled explicitly and blind - the caller does not read the job's state
            // first - so a disable aimed at a job that is already stopped, or that was never created, lands
            // here. Concurrent callers signalling at once land here for the same reason. Making the no-op
            // harmless in the entity is what allows the public API to be a plain signal rather than a
            // read-then-signal pair whose two round trips could disagree.
            //
            // Returning here also leaves the state exactly as it was found, which is what keeps
            // LastModifiedAt meaning "when this job stopped" rather than "when a stop was last signalled at
            // it". That only holds because no other operation rewrites the field on a no-op either: Run never
            // writes it, and Create rewrites it only when the batch size actually differs. Breaking either of
            // those breaks this too. It does not avoid materializing the entity - the framework persists
            // entity state after every operation, so a stop signal to an entity that does not exist yet
            // creates it with default state, which an explicit disable accepts.
            logger.BlobPurgeJobAlreadyStopped(context.Id.Key);
            return;
        }

        this.State.Status = BlobPurgeJobStatus.Pending;
        this.State.LastModifiedAt = DateTimeOffset.UtcNow;

        logger.BlobPurgeJobStopped(context.Id.Key);
    }

    /// <summary>
    /// Marks the job <see cref="BlobPurgeJobStatus.Unsupported"/> because the backend does not implement the
    /// large-payload purge RPCs.
    /// </summary>
    /// <remarks>
    /// This is a real stop, not a pause. The orchestrator reaches it after the fetch or report activity
    /// surfaces a gRPC <c>Unimplemented</c> as a <see cref="NotImplementedException"/>, and then exits; nothing
    /// restarts the job on its own, because nothing reasserts the job's existence on a timer any more.
    /// Recovery is an explicit call to the public enable API once the backend implements the RPCs -
    /// <see cref="Create"/> revives an unsupported job deliberately.
    /// </remarks>
    /// <param name="context">The entity context.</param>
    /// <param name="detail">A human-readable description of why the backend is unsupported.</param>
    public void MarkUnsupported(TaskEntityContext context, string detail)
    {
        if (this.State.Status == BlobPurgeJobStatus.Unsupported)
        {
            // Idempotent no-op. The orchestrator awaits this call and then exits, but concurrent orchestrators
            // (one per replica, all hitting the same unsupported backend) can each report before the others
            // exit. Leaving the state untouched on the repeat keeps LastModifiedAt meaning "when the job was
            // disabled" rather than "when the last replica noticed", mirroring the guard discipline on Stop.
            return;
        }

        this.State.Status = BlobPurgeJobStatus.Unsupported;
        this.State.LastError = detail;
        this.State.LastModifiedAt = DateTimeOffset.UtcNow;

        logger.BlobPurgeJobMarkedUnsupported(context.Id.Key, detail);
    }

    /// <summary>
    /// Records progress after a purge cycle completes.
    /// </summary>
    /// <param name="context">The entity context.</param>
    /// <param name="purgedCount">The number of blobs purged in the cycle.</param>
    public void RecordPurged(TaskEntityContext context, long purgedCount)
    {
        this.State.PurgedCount += purgedCount;
        this.State.LastModifiedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the current state of the auto-purge job.
    /// </summary>
    /// <param name="context">The entity context.</param>
    /// <returns>The current job state.</returns>
    public BlobPurgeJobState Get(TaskEntityContext context) => this.State;
}
