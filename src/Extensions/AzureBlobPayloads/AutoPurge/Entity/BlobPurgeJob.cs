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
    /// Creates (or reactivates) the auto-purge job. Because the job is a whole-scheduler singleton, this is
    /// intentionally a no-op when the job is already <see cref="BlobPurgeJobStatus.Active"/> so that extra
    /// client processes racing to create it do not disturb the running job.
    /// </summary>
    /// <param name="context">The entity context.</param>
    /// <param name="creationOptions">The job creation options.</param>
    public void Create(TaskEntityContext context, BlobPurgeJobCreationOptions creationOptions)
    {
        Check.NotNull(creationOptions, nameof(creationOptions));

        if (this.State.Status == BlobPurgeJobStatus.Active)
        {
            logger.BlobPurgeJobAlreadyRunning(context.Id.Key);
            return;
        }

        this.State.Status = BlobPurgeJobStatus.Active;
        this.State.PurgeBatchSize = creationOptions.PurgeBatchSize > 0 ? creationOptions.PurgeBatchSize : 500;
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
