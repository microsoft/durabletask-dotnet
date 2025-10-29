// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Durable entity that manages a history export job: lifecycle, configuration, and progress.
/// </summary>
/// <param name="logger">The logger instance.</param>
class ExportJob(ILogger<ExportJob> logger) : TaskEntity<ExportJobState>
{
    readonly ILogger<ExportJob> logger = logger;

    /// <summary>
    /// Creates a new export job or updates an existing job in-place.
    /// </summary>
    /// <param name="context">The entity context.</param>
    /// <param name="config">The export job configuration.</param>
    public void CreateOrUpdate(TaskEntityContext context, ExportJobConfig config)
    {
        Verify.NotNull(config, nameof(config));

        // Validate terminal status-only filter here if provided by caller.
        if (config.Filter?.RuntimeStatus?.Any() == true &&
            config.Filter.RuntimeStatus.Any(s => s is not (OrchestrationRuntimeStatus.Completed or OrchestrationRuntimeStatus.Failed or OrchestrationRuntimeStatus.Terminated or OrchestrationRuntimeStatus.ContinuedAsNew)))
        {
            throw new ArgumentException("Export supports terminal orchestration statuses only.");
        }

        if (this.State.Status == ExportJobStatus.Uninitialized)
        {
            this.State.Status = ExportJobStatus.Running;
            this.State.CreatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            this.State.Status = ExportJobStatus.Running;
            this.State.LastModifiedAt = DateTimeOffset.UtcNow;
        }

        this.State.Config = config;
        this.State.RefreshExecutionToken();
        this.State.LastError = null;

        // Kick off (or re-kick) the orchestrator runner guarded by execution token.
        context.ScheduleNewOrchestration(
            new TaskName(nameof(ExportJobRunnerOrchestrator)),
            new ExportJobRunRequest(context.Id, this.State.ExecutionToken));
    }

    /// <summary>
    /// Pauses the export job.
    /// </summary>
    /// <param name="context">The entity context.</param>
    public void Pause(TaskEntityContext context)
    {
        if (this.State.Status is ExportJobStatus.Uninitialized or ExportJobStatus.Deleting)
        {
            return;
        }

        this.State.Status = ExportJobStatus.Paused;
        this.State.RefreshExecutionToken();
    }

    /// <summary>
    /// Resumes the export job.
    /// </summary>
    /// <param name="context">The entity context.</param>
    public void Resume(TaskEntityContext context)
    {
        if (this.State.Status == ExportJobStatus.Deleting)
        {
            return;
        }

        this.State.Status = ExportJobStatus.Running;
        this.State.RefreshExecutionToken();

        context.ScheduleNewOrchestration(
            new TaskName(nameof(ExportJobRunnerOrchestrator)),
            new ExportJobRunRequest(context.Id, this.State.ExecutionToken));
    }

    /// <summary>
    /// Marks the export job for deletion; runner should stop and entity will be deleted by GC.
    /// </summary>
    /// <param name="context">The entity context.</param>
    public void Delete(TaskEntityContext context)
    {
        this.State.Status = ExportJobStatus.Deleting;
        this.State.RefreshExecutionToken();
        this.State = null!; // delete entity state
    }

    /// <summary>
    /// Accepts progress updates from the orchestrator/activities.
    /// </summary>
    public void AcceptProgress(TaskEntityContext context, ExportProgress progress)
    {
        Verify.NotNull(progress, nameof(progress));
        this.State.ScannedInstances += progress.ScannedInstances;
        this.State.ExportedInstances += progress.ExportedInstances;
        this.State.LastCheckpointTime = DateTimeOffset.UtcNow;
        if (progress.Watermark is not null)
        {
            this.State.Watermark = progress.Watermark;
        }
    }

    /// <summary>
    /// Records a failed instance that needs retry.
    /// </summary>
    public void AcceptFailure(TaskEntityContext context, ExportFailure failure)
    {
        Verify.NotNull(failure, nameof(failure));
        this.State.FailedInstances[failure.InstanceId] = failure with { LastAttempt = DateTimeOffset.UtcNow, AttemptCount = failure.AttemptCount + 1 };
    }

    /// <summary>
    /// Commits a checkpoint snapshot.
    /// </summary>
    public void CommitCheckpoint(TaskEntityContext context, ExportCheckpoint checkpoint)
    {
        Verify.NotNull(checkpoint, nameof(checkpoint));
        this.State.Watermark = checkpoint.Watermark ?? this.State.Watermark;
        this.State.LastCheckpointTime = DateTimeOffset.UtcNow;
    }
}

