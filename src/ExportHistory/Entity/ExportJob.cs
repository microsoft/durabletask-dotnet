// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Durable entity that manages a history export job: lifecycle, configuration, and progress.
/// </summary>
/// <param name="logger">The logger instance.</param>
class ExportJob(ILogger<ExportJob> logger) : TaskEntity<ExportJobState>
{
    /// <summary>
    /// Creates a new export job from creation options.
    /// </summary>
    /// <param name="context">The entity context.</param>
    /// <param name="creationOptions">The export job creation options.</param>
    /// <exception cref="ArgumentNullException">Thrown when creationOptions is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when invalid state transition is attempted or export job already exists.</exception>
    public void Create(TaskEntityContext context, ExportJobCreationOptions creationOptions)
    {
        try
        {
            Check.NotNull(creationOptions, nameof(creationOptions));

            if (!this.CanTransitionTo(nameof(this.Create), ExportJobStatus.Active))
            {
                throw new ExportJobInvalidTransitionException(
                    creationOptions.JobId,
                    this.State.Status,
                    ExportJobStatus.Active,
                    nameof(this.Create));
            }

            // Convert ExportJobCreationOptions to ExportJobConfiguration
            // Note: RuntimeStatus validation already done in ExportJobCreationOptions constructor
            // Note: Destination should be populated by the client before reaching here
            Verify.NotNull(creationOptions.Destination, nameof(creationOptions.Destination));
            
            ExportJobConfiguration config = new ExportJobConfiguration(
                Mode: creationOptions.Mode,
                Filter: new ExportFilter(
                    CompletedTimeFrom: creationOptions.CompletedTimeFrom,
                    CompletedTimeTo: creationOptions.CompletedTimeTo,
                    RuntimeStatus: creationOptions.RuntimeStatus),
                Destination: creationOptions.Destination,
                Format: creationOptions.Format,
                MaxInstancesPerBatch: creationOptions.MaxInstancesPerBatch);

            this.State.Config = config;
            this.State.Status = ExportJobStatus.Active;
            this.State.CreatedAt = this.State.LastModifiedAt = DateTimeOffset.UtcNow;
            this.State.LastError = null;

            logger.CreatedExportJob(creationOptions.JobId);

            // Signal the Run method to start the export
            context.SignalEntity(
                context.Id,
                nameof(this.Run));
        }
        catch (Exception ex)
        {
            logger.ExportJobOperationError(
                creationOptions?.JobId ?? string.Empty,
                nameof(this.Create),
                "Failed to create export job",
                ex);
            throw;
        }
    }

    /// <summary>
    /// Gets the current state of the export job.
    /// </summary>
    /// <param name="context">The entity context.</param>
    /// <returns>The current export job state.</returns>
    public ExportJobState Get(TaskEntityContext context)
    {
        return this.State;
    }

    /// <summary>
    /// Runs the export job by starting the export orchestrator.
    /// </summary>
    /// <param name="context">The entity context.</param>
    /// <exception cref="InvalidOperationException">Thrown when export job is not in Active status.</exception>
    public void Run(TaskEntityContext context)
    {
        try
        {
            Verify.NotNull(this.State.Config, nameof(this.State.Config));

            if (this.State.Status != ExportJobStatus.Active)
            {
                string errorMessage = "Export job must be in Active status to run.";
                logger.ExportJobOperationError(context.Id.Key, nameof(this.Run), errorMessage, new InvalidOperationException(errorMessage));
                throw new InvalidOperationException(errorMessage);
            }

            this.StartExportOrchestration(context);
        }
        catch (Exception ex)
        {
            logger.ExportJobOperationError(
                context.Id.Key,
                nameof(this.Run),
                "Failed to run export job",
                ex);
            throw;
        }
    }

    void StartExportOrchestration(TaskEntityContext context)
    {
        try
        {
            // Use a fixed instance ID based on job ID to ensure only one orchestrator runs per job
            // This prevents concurrent orchestrators if Run is called multiple times
            string instanceId = ExportHistoryConstants.GetOrchestratorInstanceId(context.Id.Key);
            StartOrchestrationOptions startOrchestrationOptions = new StartOrchestrationOptions(instanceId);

            logger.ExportJobOperationInfo(
                context.Id.Key,
                nameof(this.StartExportOrchestration),
                $"Starting new orchestration named '{nameof(ExportJobOrchestrator)}' with instance ID: {instanceId}");

            context.ScheduleNewOrchestration(
                new TaskName(nameof(ExportJobOrchestrator)),
                new ExportJobRunRequest(context.Id),
                startOrchestrationOptions);

            this.State.OrchestratorInstanceId = instanceId;
            this.State.LastModifiedAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            // Mark job as failed and record the exception
            this.State.Status = ExportJobStatus.Failed;
            this.State.LastError = ex.Message;
            this.State.LastModifiedAt = DateTimeOffset.UtcNow;

            logger.ExportJobOperationError(
                context.Id.Key,
                nameof(this.StartExportOrchestration),
                "Failed to start export orchestration",
                ex);
        }
    }

    bool CanTransitionTo(string operationName, ExportJobStatus targetStatus)
    {
        return ExportJobTransitions.IsValidTransition(operationName, this.State.Status, targetStatus);
    }

    /// <summary>
    /// Commits a checkpoint snapshot with progress updates and optional failures.
    /// </summary>
    /// <param name="context">The entity context.</param>
    /// <param name="request">The checkpoint commit request containing progress, checkpoint, and failures.</param>
    public void CommitCheckpoint(TaskEntityContext context, CommitCheckpointRequest request)
    {
        Verify.NotNull(request, nameof(request));

        // Update progress counts
        this.State.ScannedInstances += request.ScannedInstances;
        this.State.ExportedInstances += request.ExportedInstances;

        // Update checkpoint if provided (successful batch moves cursor forward)
        // If null (failed batch), keep current checkpoint to not move cursor forward
        if (request.Checkpoint is not null)
        {
            this.State.Checkpoint = request.Checkpoint;
        }

        // Update checkpoint time and last modified time
        this.State.LastCheckpointTime = this.State.LastModifiedAt = DateTimeOffset.UtcNow;

        // If there are failures and checkpoint is null (batch failed), mark job as failed
        if (request.Checkpoint is null && request.Failures != null && request.Failures.Count > 0)
        {
            this.State.Status = ExportJobStatus.Failed;
            string failureSummary = string.Join("; ", request.Failures.Select(f => $"{f.InstanceId}: {f.Reason}"));
            this.State.LastError = $"Batch export failed after retries. Failures: {failureSummary}";
        }
    }

    /// <summary>
    /// Marks the export job as completed.
    /// </summary>
    /// <param name="context">The entity context.</param>
    /// <exception cref="InvalidOperationException">Thrown when invalid state transition is attempted.</exception>
    public void MarkAsCompleted(TaskEntityContext context)
    {
        try
        {
            if (!this.CanTransitionTo(nameof(this.MarkAsCompleted), ExportJobStatus.Completed))
            {
                throw new ExportJobInvalidTransitionException(
                    context.Id.Key,
                    this.State.Status,
                    ExportJobStatus.Completed,
                    nameof(this.MarkAsCompleted));
            }

            this.State.Status = ExportJobStatus.Completed;
            this.State.LastModifiedAt = DateTimeOffset.UtcNow;
            this.State.LastError = null;

            logger.ExportJobOperationInfo(
                context.Id.Key,
                nameof(this.MarkAsCompleted),
                "Export job marked as completed");
        }
        catch (Exception ex)
        {
            logger.ExportJobOperationError(
                context.Id.Key,
                nameof(this.MarkAsCompleted),
                "Failed to mark export job as completed",
                ex);
            throw;
        }
    }

    /// <summary>
    /// Marks the export job as failed.
    /// </summary>
    /// <param name="context">The entity context.</param>
    /// <param name="errorMessage">The error message describing why the job failed.</param>
    /// <exception cref="InvalidOperationException">Thrown when invalid state transition is attempted.</exception>
    public void MarkAsFailed(TaskEntityContext context, string? errorMessage = null)
    {
        try
        {
            if (!this.CanTransitionTo(nameof(this.MarkAsFailed), ExportJobStatus.Failed))
            {
                throw new ExportJobInvalidTransitionException(
                    context.Id.Key,
                    this.State.Status,
                    ExportJobStatus.Failed,
                    nameof(this.MarkAsFailed));
            }

            this.State.Status = ExportJobStatus.Failed;
            this.State.LastError = errorMessage;
            this.State.LastModifiedAt = DateTimeOffset.UtcNow;

            logger.ExportJobOperationInfo(
                context.Id.Key,
                nameof(this.MarkAsFailed),
                $"Export job marked as failed: {errorMessage ?? "Unknown error"}");
        }
        catch (Exception ex)
        {
            logger.ExportJobOperationError(
                context.Id.Key,
                nameof(this.MarkAsFailed),
                "Failed to mark export job as failed",
                ex);
            throw;
        }
    }

    /// <summary>
    /// Deletes the export job entity.
    /// </summary>
    /// <param name="context">The entity context.</param>
    public void Delete(TaskEntityContext context)
    {
        try
        {
            logger.ExportJobOperationInfo(
                context.Id.Key,
                nameof(this.Delete),
                "Deleting export job entity");

            // Delete the entity by setting state to null
            // This is the standard way to delete a durable entity
            this.State = null!;
        }
        catch (Exception ex)
        {
            logger.ExportJobOperationError(
                context.Id.Key,
                nameof(this.Delete),
                "Failed to delete export job entity",
                ex);
            throw;
        }
    }
}
