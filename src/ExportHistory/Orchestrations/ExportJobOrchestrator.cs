// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Orchestrator input to start a runner for a given job.
/// </summary>
public sealed record ExportJobRunRequest(EntityInstanceId JobEntityId);

/// <summary>
/// Orchestrator that performs the actual export work by querying orchestration instances
/// and exporting their history to blob storage.
/// </summary>
[DurableTask]
public class ExportJobOrchestrator : TaskOrchestrator<ExportJobRunRequest, object?>
{
    readonly ILogger<ExportJobOrchestrator> logger;
    const int MaxRetryAttempts = 3;
    const int MinBackoffSeconds = 60; // 1 minute
    const int MaxBackoffSeconds = 300; // 5 minutes

    /// <summary>
    /// Initializes a new instance of the <see cref="ExportJobOrchestrator"/> class.
    /// </summary>
    public ExportJobOrchestrator(ILogger<ExportJobOrchestrator> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public override async Task<object?> RunAsync(TaskOrchestrationContext context, ExportJobRunRequest input)
    {
        string jobId = input.JobEntityId.Key;
        this.logger.ExportJobOperationInfo(jobId, nameof(ExportJobOrchestrator), "Export orchestrator started");

        try
        {
            // Get the export job state and configuration from the entity
            ExportJobState? jobState = await context.Entities.CallEntityAsync<ExportJobState?>(
                input.JobEntityId,
                ExportJobOperations.Get,
                null);

            if (jobState == null || jobState.Config == null)
            {
                throw new InvalidOperationException($"Export job '{jobId}' not found or has no configuration.");
            }

            // Check if job is still active
            if (jobState.Status != ExportJobStatus.Active)
            {
                this.logger.ExportJobOperationWarning(jobId, nameof(ExportJobOrchestrator), $"Job status is {jobState.Status}, not Active - orchestrator cancelled");
                return null;
            }

            ExportJobConfiguration config = jobState.Config;

            // Process instances in batches using explicit loop state
            bool hasMore = true;
            while (hasMore)
            {
                // Check if job is still active (entity might have been deleted or failed)
                ExportJobState? currentState = await context.Entities.CallEntityAsync<ExportJobState?>(
                    input.JobEntityId,
                    ExportJobOperations.Get,
                    null);

                if (currentState == null || 
                    currentState.Status != ExportJobStatus.Active)
                {
                    this.logger.ExportJobOperationWarning(jobId, nameof(ExportJobOrchestrator), "Job is no longer active - orchestrator cancelled");
                    hasMore = false;
                    continue;
                }

                // Call activity to list terminal instances with only necessary information
                ListTerminalInstancesRequest listRequest = new ListTerminalInstancesRequest(
                    CreatedTimeFrom: currentState.Config.Filter.CreatedTimeFrom,
                    CreatedTimeTo: currentState.Config.Filter.CreatedTimeTo,
                    RuntimeStatus: currentState.Config.Filter.RuntimeStatus,
                    ContinuationToken: currentState.Checkpoint?.ContinuationToken,
                    MaxInstancesPerBatch: currentState.Config.MaxInstancesPerBatch);

                InstancePage pageResult = await context.CallActivityAsync<InstancePage>(
                    nameof(ListTerminalInstancesActivity),
                    listRequest);

                // Handle empty page result - no instances found, treat as end of data
                if (pageResult == null || pageResult.InstanceIds.Count == 0)
                {
                    if (config.Mode == ExportMode.Batch)
                    {
                        hasMore = false;
                        continue;
                    }
                    await context.CreateTimer(TimeSpan.FromMinutes(5), default);
                    continue;
                }

                List<string> instancesToExport = pageResult.InstanceIds;
                long scannedCount = instancesToExport.Count;

                // Process batch with retry logic
                BatchExportResult batchResult = await this.ProcessBatchWithRetryAsync(
                    context,
                    input.JobEntityId,
                    instancesToExport,
                    config);

                // Commit checkpoint based on batch result
                if (batchResult.AllSucceeded)
                {
                    // All exports succeeded - commit with checkpoint to move cursor forward
                    await this.CommitCheckpointAsync(
                        context,
                        input.JobEntityId,
                        scannedInstances: scannedCount,
                        exportedInstances: batchResult.ExportedCount,
                        checkpoint: pageResult.NextCheckpoint,
                        failures: null);
                }
                else
                {
                    // Batch failed after all retries - commit without checkpoint (don't move cursor), record failures
                    await this.CommitCheckpointAsync(
                        context,
                        input.JobEntityId,
                        scannedInstances: scannedCount,
                        exportedInstances: batchResult.ExportedCount,
                        checkpoint: null,
                        failures: batchResult.Failures);

                    // Job is now marked as failed in the entity, stop processing
                    hasMore = false;
                    continue;
                }
            }

            await this.MarkAsCompletedAsync(context, input.JobEntityId);

            this.logger.ExportJobOperationInfo(jobId, nameof(ExportJobOrchestrator), "Export orchestrator completed");
            return null!;
        }
        catch (Exception ex)
        {
            this.logger.ExportJobOperationError(jobId, nameof(ExportJobOrchestrator), "Export orchestrator failed", ex);
            
            await this.MarkAsFailedAsync(context, input.JobEntityId, ex.Message);
            
            throw;
        }
    }

    async Task<BatchExportResult> ProcessBatchWithRetryAsync(
        TaskOrchestrationContext context,
        EntityInstanceId jobEntityId,
        List<string> instanceIds,
        ExportJobConfiguration config)
    {
        string jobId = jobEntityId.Key;
        
        for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            this.logger.ExportJobOperationInfo(
                jobId,
                nameof(ProcessBatchWithRetryAsync),
                $"Processing batch of {instanceIds.Count} instances (attempt {attempt}/{MaxRetryAttempts})");

            // Export all instances in the batch
            List<ExportResult> results = await this.ExportBatchAsync(context, instanceIds, config);

            // Check if all exports succeeded
            List<ExportResult> failedResults = results.Where(r => !r.Success).ToList();
            
            if (failedResults.Count == 0)
            {
                // All exports succeeded
                int exportedCount = results.Count;
                this.logger.ExportJobOperationInfo(
                    jobId,
                    nameof(ProcessBatchWithRetryAsync),
                    $"Batch export succeeded on attempt {attempt} - exported {exportedCount} instances");
                
                return new BatchExportResult
                {
                    AllSucceeded = true,
                    ExportedCount = exportedCount,
                    Failures = null,
                };
            }

            // Some exports failed
            this.logger.ExportJobOperationWarning(
                jobId,
                nameof(ProcessBatchWithRetryAsync),
                $"Batch export failed on attempt {attempt} - {failedResults.Count} failures out of {instanceIds.Count} instances");

            // If this is the last attempt, return failures
            if (attempt == MaxRetryAttempts)
            {
                List<ExportFailure> failures = failedResults.Select(r => new ExportFailure(
                    InstanceId: r.InstanceId,
                    Reason: r.Error ?? "Unknown error",
                    AttemptCount: attempt,
                    LastAttempt: DateTimeOffset.UtcNow)).ToList();

                int exportedCount = results.Count(r => r.Success);
                
                return new BatchExportResult
                {
                    AllSucceeded = false,
                    ExportedCount = exportedCount,
                    Failures = failures,
                };
            }

            // Calculate exponential backoff: 1min, 2min, 4min (capped at 5min)
            int backoffSeconds = Math.Min(MinBackoffSeconds * (int)Math.Pow(2, attempt - 1), MaxBackoffSeconds);
            TimeSpan backoffDelay = TimeSpan.FromSeconds(backoffSeconds);

            this.logger.ExportJobOperationInfo(
                jobId,
                nameof(ProcessBatchWithRetryAsync),
                $"Retrying batch export after {backoffDelay.TotalMinutes:F1} minutes (attempt {attempt + 1}/{MaxRetryAttempts})");

            // Wait before retrying
            await context.CreateTimer(backoffDelay, default);
        }

        // Should not reach here, but return empty result if we do
        return new BatchExportResult
        {
            AllSucceeded = false,
            ExportedCount = 0,
            Failures = new List<ExportFailure>(),
        };
    }

    async Task<List<ExportResult>> ExportBatchAsync(
        TaskOrchestrationContext context,
        List<string> instanceIds,
        ExportJobConfiguration config)
    {
        List<ExportResult> results = new();
        List<Task<ExportResult>> exportTasks = new();

        foreach (string instanceId in instanceIds)
        {
            // Create export request with destination and format
            ExportRequest exportRequest = new ExportRequest
            {
                InstanceId = instanceId,
                Destination = config.Destination,
                Format = config.Format,
            };

            exportTasks.Add(
                context.CallActivityAsync<ExportResult>(
                    nameof(ExportInstanceHistoryActivity),
                    exportRequest));

            // Limit parallel export activities
            if (exportTasks.Count >= config.MaxParallelExports)
            {
                ExportResult[] batchResults = await Task.WhenAll(exportTasks);
                results.AddRange(batchResults);
                exportTasks.Clear();
            }
        }

        // Wait for remaining export activities
        if (exportTasks.Count > 0)
        {
            ExportResult[] batchResults = await Task.WhenAll(exportTasks);
            results.AddRange(batchResults);
        }

        return results;
    }

    async Task CommitCheckpointAsync(
        TaskOrchestrationContext context,
        EntityInstanceId jobEntityId,
        long scannedInstances,
        long exportedInstances,
        ExportCheckpoint? checkpoint,
        List<ExportFailure>? failures)
    {
        CommitCheckpointRequest request = new CommitCheckpointRequest
        {
            ScannedInstances = scannedInstances,
            ExportedInstances = exportedInstances,
            Checkpoint = checkpoint,
            Failures = failures,
        };

        await context.Entities.CallEntityAsync(
            jobEntityId,
            nameof(ExportJob.CommitCheckpoint),
            request);
    }

    async Task MarkAsCompletedAsync(
        TaskOrchestrationContext context,
        EntityInstanceId jobEntityId)
    {
        await context.Entities.CallEntityAsync(
            jobEntityId,
            nameof(ExportJob.MarkAsCompleted),
            null);
    }

    async Task MarkAsFailedAsync(
        TaskOrchestrationContext context,
        EntityInstanceId jobEntityId,
        string? errorMessage)
    {
        await context.Entities.CallEntityAsync(
            jobEntityId,
            nameof(ExportJob.MarkAsFailed),
            errorMessage);
    }

    sealed class BatchExportResult
    {
        public bool AllSucceeded { get; set; }
        public int ExportedCount { get; set; }
        public List<ExportFailure>? Failures { get; set; }
    }
}
