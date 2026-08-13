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
    /// a per-task-hub singleton, client processes racing to create it converge on the same result rather than
    /// disturbing a running job. It also takes the batch size when the job is already
    /// <see cref="BlobPurgeJobStatus.Active"/>, which is what lets a configuration change reach a running job.
    /// </summary>
    /// <param name="context">The entity context.</param>
    /// <param name="purgeBatchSize">
    /// The maximum number of tombstoned payloads to request from the backend per cycle.
    /// </param>
    public void Create(TaskEntityContext context, int purgeBatchSize)
    {
        if (this.State.Status == BlobPurgeJobStatus.Active)
        {
            // The batch size is taken because this is the only path by which a changed configuration reaches an
            // active job - the orchestrator re-reads it from here every cycle. Without this the value written by
            // the very first Create would be the only one the job ever used, and a batch size the backend
            // rejects would wedge it permanently.
            this.State.PurgeBatchSize = purgeBatchSize;
            this.State.LastModifiedAt = DateTimeOffset.UtcNow;

            logger.BlobPurgeJobAlreadyRunning(context.Id.Key);

            // Run is re-signalled even though the job is already active, and this is what makes the job
            // self-heal: Create runs on every host start, so a job whose orchestrator has died is rebuilt at the
            // next one. The signal is deliberately blind. An entity-initiated start carries no reuse policy, so
            // the backend decides its fate: it discards the start while the target instance exists in any
            // non-completed status, and purges and replaces it once the instance has completed, terminated,
            // failed or been canceled (its OkToPurge / IsCompleted rule). A healthy orchestrator is therefore
            // left strictly alone and only a dead one is replaced.
            //
            // Being blind is the point, not a shortcut. The backend reaches that decision atomically within one
            // partition operation, so delegating it removes the race entirely. Checking the orchestrator's
            // status here and signalling only when it looked dead would reintroduce the window in which it dies
            // - or recovers - between the read and the signal, which is strictly worse than not asking.
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
    /// The orchestrator runs under a fixed instance ID, which is what keeps the singleton a singleton. No reuse
    /// policy is passed, and none can be: the entity's start action has no field to carry one, so anything set
    /// here would be dropped before it reached the wire. That default is the behaviour the job relies on rather
    /// than an omission - the backend discards a start aimed at an instance that already exists in a
    /// non-completed status, and replaces the instance only once it has completed, terminated, failed or been
    /// canceled. Signalling this operation is therefore always safe, whatever the orchestrator is doing.
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

        this.State.LastModifiedAt = DateTimeOffset.UtcNow;
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
            // Load-bearing, and NOT redundant with the client-side pre-check in BlobPurgeJobStarter that
            // normally prevents this operation from being signalled at all. That pre-check reads the job state
            // and then signals: two separate round trips with nothing holding the state still between them, so
            // the job can stop - or never have existed - in the gap. This guard is what makes losing that race
            // harmless, which is the only reason the pre-check is allowed to be a plain read. Deleting it
            // because "the caller already checked" would reintroduce exactly the window it was written to
            // absorb. Concurrent hosts signalling at once land here for the same reason.
            //
            // Returning here also leaves the state exactly as it was found. That preserves LastModifiedAt,
            // whose only useful meaning is when the job actually stopped; rewriting it on a redundant stop
            // would destroy that. It does not avoid materializing the entity - the framework persists entity
            // state after every operation, so a stop signal to an entity that does not exist yet creates it
            // with default state. Not creating it is the pre-check's job, not this guard's.
            logger.BlobPurgeJobAlreadyStopped(context.Id.Key);
            return;
        }

        this.State.Status = BlobPurgeJobStatus.Pending;
        this.State.LastModifiedAt = DateTimeOffset.UtcNow;

        logger.BlobPurgeJobStopped(context.Id.Key);
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
