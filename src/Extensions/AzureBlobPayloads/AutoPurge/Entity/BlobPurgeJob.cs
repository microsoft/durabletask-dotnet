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
        Check.Argument(
            purgeBatchSize > 0 && purgeBatchSize <= BlobPurgeConstants.MaxBatchSize,
            nameof(purgeBatchSize),
            "Purge batch size is out of range.");
        if (this.State.Status == BlobPurgeJobStatus.Active)
        {
            Check.NotNullOrEmpty(this.State.Generation);

            // The batch size is taken because this is the only path by which a changed size reaches an active
            // job - the orchestrator re-reads it from here every cycle. Without this the value written by the
            // very first Create would be the only one the job ever used, and a batch size the backend rejects
            // would wedge it permanently.
            //
            // Only resizing changes LastModifiedAt; repeated enables retain the activation's generation.
            if (this.State.PurgeBatchSize != purgeBatchSize)
            {
                this.State.PurgeBatchSize = purgeBatchSize;
                this.State.LastModifiedAt = DateTimeOffset.UtcNow;
            }

            logger.BlobPurgeJobAlreadyRunning(context.Id.Key);

            // Repeated enables reuse this generation's ID: the backend preserves a running instance and can
            // replace a completed one.
            context.SignalEntity(context.Id, nameof(this.Run));
            return;
        }

        this.State.Status = BlobPurgeJobStatus.Active;
        this.State.Generation = Guid.NewGuid().ToString("N");
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
    /// Each activation has its own runner ID. Re-enabling therefore does not lose its start to an older
    /// activation that is still stopping. Repeated Run signals target the current generation and the backend
    /// deduplicates them while that runner is alive.
    /// Older in-flight cycles may overlap this generation; generation checks retire them on their next read.
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

        string generation = Check.NotNullOrEmpty(this.State.Generation);
        Check.Argument(
            this.State.PurgeBatchSize > 0 && this.State.PurgeBatchSize <= BlobPurgeConstants.MaxBatchSize,
            nameof(this.State.PurgeBatchSize),
            "Purge batch size is out of range.");
        string instanceId = BlobPurgeConstants.GetOrchestratorInstanceId(context.Id.Key, generation);
        StartOrchestrationOptions startOrchestrationOptions = new(instanceId);

        context.ScheduleNewOrchestration(
            new TaskName(nameof(BlobPurgeJobOrchestrator)),
            new BlobPurgeJobRunRequest(context.Id, this.State.PurgeBatchSize, generation),
            startOrchestrationOptions);
    }

    /// <summary>
    /// Stops the auto-purge job.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The perpetual orchestrator is deliberately not terminated here, and this operation does not touch its
    /// instance ID at all. The orchestrator reads this entity at the top of every cycle and exits on its own
    /// once the job is no longer <see cref="BlobPurgeJobStatus.Active"/> or its generation changes. Shutdown is
    /// cooperative: the in-flight cycle may finish alongside a re-enabled generation rather than being cut off.
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
    /// Disables only the active generation that observed an unsupported backend.
    /// </summary>
    /// <param name="context">The entity context.</param>
    /// <param name="request">The generation and observed failure detail.</param>
    public void MarkGenerationUnsupported(TaskEntityContext context, BlobPurgeUnsupportedRequest request)
    {
        Check.NotNull(request);
        Check.NotNullOrEmpty(request.Generation);
        if (this.State.Status != BlobPurgeJobStatus.Active
            || !string.Equals(this.State.Generation, request.Generation, StringComparison.Ordinal))
        {
            return;
        }

        this.State.Status = BlobPurgeJobStatus.Unsupported;
        this.State.LastError = request.Detail;
        this.State.LastModifiedAt = DateTimeOffset.UtcNow;
        logger.BlobPurgeJobMarkedUnsupported(context.Id.Key, request.Detail);
    }

    /// <summary>
    /// Records diagnostic progress after a purge cycle, including work completed by a retiring generation.
    /// </summary>
    /// <param name="context">The entity context.</param>
    /// <param name="purgedCount">The reported terminal-success count; duplicates are not deduplicated.</param>
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
