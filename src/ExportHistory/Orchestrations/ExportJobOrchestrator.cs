// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Orchestrator input to start a runner for a given job.
/// </summary>
public sealed record ExportJobRunRequest(EntityInstanceId JobEntityId, int ProcessedCycles = 0);

/// <summary>
/// Orchestrator that performs the actual export work by querying orchestration instances
/// and exporting their history to blob storage.
/// </summary>
[DurableTask]
public class ExportJobOrchestrator : TaskOrchestrator<ExportJobRunRequest, object?>
{
    const int MaxRetryAttempts = 3;
    const int MinBackoffSeconds = 60; // 1 minute
    const int MaxBackoffSeconds = 300; // 5 minutes
    const int ContinueAsNewFrequency = 5;
    static readonly TimeSpan ContinuousExportIdleDelay = TimeSpan.FromMinutes(1);

    // Retry policy for individual export activities: 3 attempts with exponential backoff
    // First retry after 15s, second retry after 30s (capped at 60s)
    static readonly RetryPolicy ExportActivityRetryPolicy = new(
        maxNumberOfAttempts: 3,
        firstRetryInterval: TimeSpan.FromSeconds(15),
        backoffCoefficient: 2.0,
        maxRetryInterval: TimeSpan.FromSeconds(60));

    /// <inheritdoc/>
    public override async Task<object?> RunAsync(TaskOrchestrationContext context, ExportJobRunRequest input)
    {
        ILogger logger = context.CreateReplaySafeLogger<ExportJobOrchestrator>();
        string jobId = input.JobEntityId.Key;
        logger.ExportJobOperationInfo(jobId, nameof(ExportJobOrchestrator), "Export orchestrator started");

        try
        {
            // Get the export job state and configuration from the entity
            ExportJobState? jobState = await context.Entities.CallEntityAsync<ExportJobState?>(
                input.JobEntityId,
                nameof(ExportJob.Get),
                null);

            if (jobState == null || jobState.Config == null)
            {
                throw new InvalidOperationException($"Export job '{jobId}' not found or has no configuration.");
            }

            // Check if job is still active
            if (jobState.Status != ExportJobStatus.Active)
            {
                logger.ExportJobOperationWarning(jobId, nameof(ExportJobOrchestrator), $"Job status is {jobState.Status}, not Active - orchestrator cancelled");
                return null;
            }

            ExportJobConfiguration config = jobState.Config;

            int processedCycles = input.ProcessedCycles;

            while (true)
            {
                processedCycles++;
                if (processedCycles > ContinueAsNewFrequency)
                {
                    context.ContinueAsNew(new ExportJobRunRequest(input.JobEntityId, ProcessedCycles: 0));
                    return null!;
                }

                // Check if job is still active (entity might have been deleted or failed)
                ExportJobState? currentState = await context.Entities.CallEntityAsync<ExportJobState?>(
                    input.JobEntityId,
                    nameof(ExportJob.Get),
                    null);

                if (currentState == null ||
                    currentState.Config == null ||
                    currentState.Status != ExportJobStatus.Active)
                {
                    logger.ExportJobOperationWarning(jobId, nameof(ExportJobOrchestrator), "Job is no longer active");
                    return null;
                }

                // if (currentState.Checkpoint is not null &&
                //     string.IsNullOrEmpty(currentState.Checkpoint.LastInstanceKey))
                // {
                //     if (config.Mode == ExportMode.Batch)
                //     {
                //         logger.ExportJobOperationInfo(jobId, nameof(ExportJobOrchestrator), "No more instances to export - export complete.");
                //         break;
                //     }
                //     else if (config.Mode == ExportMode.Continuous)
                //     {
                //         logger.ExportJobOperationInfo(jobId, nameof(ExportJobOrchestrator), "No more instances to export currently - will check again later.");
                //         await context.CreateTimer(ContinuousExportIdleDelay, default);
                //         continue;
                //     }
                //     else
                //     {
                //         throw new InvalidOperationException("Invalid export mode.");
                //     }
                // }

                // Call activity to list terminal instances with only necessary information
                ListTerminalInstancesRequest listRequest = new ListTerminalInstancesRequest(
                    CompletedTimeFrom: currentState.Config.Filter.CompletedTimeFrom,
                    CompletedTimeTo: currentState.Config.Filter.CompletedTimeTo,
                    RuntimeStatus: currentState.Config.Filter.RuntimeStatus,
                    LastInstanceKey: currentState.Checkpoint?.LastInstanceKey,
                    MaxInstancesPerBatch: currentState.Config.MaxInstancesPerBatch);

                InstancePage pageResult = await context.CallActivityAsync<InstancePage>(
                    nameof(ListTerminalInstancesActivity),
                    listRequest);

                List<string> instancesToExport = pageResult.InstanceIds;
                long scannedCount = instancesToExport.Count;

                if (scannedCount == 0)
                {
                    if (config.Mode == ExportMode.Continuous)
                    {
                        logger.ExportJobOperationInfo(jobId, nameof(ExportJobOrchestrator), "No more instances to export - export complete.");
                        await context.CreateTimer(ContinuousExportIdleDelay, default);
                        continue;
                    }
                    else if (config.Mode == ExportMode.Batch)
                    {
                        logger.ExportJobOperationInfo(jobId, nameof(ExportJobOrchestrator), "No more instances to export currently - will retry later.");
                        break;
                    }
                    else
                    {
                        throw new InvalidOperationException("Invalid export mode.");
                    }
                }

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
                        scannedInstances: 0,
                        exportedInstances: 0,
                        checkpoint: null,
                        failures: batchResult.Failures);

                    // Throw detailed exception with failure information
                    int failureCount = batchResult.Failures?.Count ?? 0;
                    string failureDetails;
                    if (batchResult.Failures != null && batchResult.Failures.Count > 0)
                    {
                        failureDetails = string.Join(
                            "; ",
                            batchResult.Failures
                                .Take(10)
                                .Select(f =>
                                    $"InstanceId: {f.InstanceId}, Reason: {f.Reason}"));
                    }
                    else
                    {
                        failureDetails = "No failure details available";
                    }

                    if (batchResult.Failures != null && batchResult.Failures.Count > 10)
                    {
                        failureDetails += $" ... and {batchResult.Failures.Count - 10} more failures";
                    }

                    throw new InvalidOperationException(
                        $"Export job '{jobId}' batch export failed after {MaxRetryAttempts} retry attempts. " +
                        $"Failure details: {failureDetails}");
                }
            }

            await this.MarkAsCompletedAsync(context, input.JobEntityId);

            logger.ExportJobOperationInfo(jobId, nameof(ExportJobOrchestrator), "Export orchestrator completed");
            return null!;
        }
        catch (Exception ex)
        {
            logger.ExportJobOperationError(jobId, nameof(ExportJobOrchestrator), "Export orchestrator failed", ex);

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
        ILogger logger = context.CreateReplaySafeLogger<ExportJobOrchestrator>();
        string jobId = jobEntityId.Key;

        for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            logger.ExportJobOperationInfo(
                jobId,
                nameof(this.ProcessBatchWithRetryAsync),
                $"Processing batch of {instanceIds.Count} instances (attempt {attempt}/{MaxRetryAttempts})");

            // Export all instances in the batch
            List<ExportResult> results = await this.ExportBatchAsync(context, instanceIds, config);

            // Check if all exports succeeded
            List<ExportResult> failedResults = results.Where(r => !r.Success).ToList();

            if (failedResults.Count == 0)
            {
                // All exports succeeded
                int exportedCount = results.Count;
                logger.ExportJobOperationInfo(
                    jobId,
                    nameof(this.ProcessBatchWithRetryAsync),
                    $"Batch export succeeded on attempt {attempt} - exported {exportedCount} instances");

                return new BatchExportResult
                {
                    AllSucceeded = true,
                    ExportedCount = exportedCount,
                    Failures = null,
                };
            }

            // Some exports failed
            logger.ExportJobOperationWarning(
                jobId,
                nameof(this.ProcessBatchWithRetryAsync),
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

            logger.ExportJobOperationInfo(
                jobId,
                nameof(this.ProcessBatchWithRetryAsync),
                $"Retrying batch export after {backoffDelay.TotalMinutes:F1} minutes (attempt {attempt + 1}/{MaxRetryAttempts})");

            // Wait before retrying
            await context.CreateTimer(backoffDelay, default);
        }

        // Should not reach here, but return empty result if we do
        return new BatchExportResult
        {
            AllSucceeded = true,
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

            // Use retry policy for individual export activities (up to 3 attempts)
            exportTasks.Add(
                context.CallActivityAsync<ExportResult>(
                    nameof(ExportInstanceHistoryActivity),
                    exportRequest,
                    new TaskOptions(ExportActivityRetryPolicy)));

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
