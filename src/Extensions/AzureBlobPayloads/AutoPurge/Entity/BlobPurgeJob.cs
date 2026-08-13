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
    /// Creates (or reactivates) the auto-purge job. Because the job is a per-task-hub singleton, this does not
    /// restart a job that is already <see cref="BlobPurgeJobStatus.Active"/> so that extra client processes
    /// racing to create it do not disturb the running job. It does still take the batch size in that case, which
    /// is what lets a configuration change reach a job that is already running.
    /// </summary>
    /// <param name="context">The entity context.</param>
    /// <param name="purgeBatchSize">
    /// The maximum number of tombstoned payloads to request from the backend per cycle.
    /// </param>
    public void Create(TaskEntityContext context, int purgeBatchSize)
    {
        if (this.State.Status == BlobPurgeJobStatus.Active)
        {
            // Deliberately not re-signalling Run: the orchestrator is already up, and starting a second one
            // over a live one is destructive. The batch size is still taken, because this is the only path by
            // which a changed configuration reaches an active job - the orchestrator re-reads it from here
            // every cycle. Without this the value written by the very first Create would be the only one the
            // job ever used, and a batch size the backend rejects would wedge it permanently.
            this.State.PurgeBatchSize = purgeBatchSize;
            this.State.LastModifiedAt = DateTimeOffset.UtcNow;

            logger.BlobPurgeJobAlreadyRunning(context.Id.Key);
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
    /// Starts the purge orchestrator if the job is active. Uses a fixed orchestrator instance ID so only one
    /// orchestrator ever runs for the singleton job.
    /// </summary>
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
