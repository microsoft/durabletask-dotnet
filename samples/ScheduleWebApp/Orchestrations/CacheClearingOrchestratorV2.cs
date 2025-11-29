// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using ScheduleWebApp.Activities;

namespace ScheduleWebApp.Orchestrations;

public class CacheClearingOrchestratorV2 : TaskOrchestrator<string, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, string scheduleId)
    {
        ILogger logger = context.CreateReplaySafeLogger(nameof(CacheClearingOrchestratorV2));
        try
        {
            logger.LogInformation("Starting CacheClearingOrchestration for schedule ID: {ScheduleId}", scheduleId);

            TaskOptions options = new TaskOptions(tags: new Dictionary<string, string>
            {
                { "scheduleId", scheduleId }
            });

            // Schedule all activities first to ensure deterministic ordering
            Task<string>[] tasks = Enumerable.Range(0, 100)
                .Select(i => context.CallActivityAsync<string>(nameof(CacheClearingActivity), new string('A', 4 * 1024), options))
                .ToArray();

            await Task.WhenAll(tasks);

            logger.LogInformation("CacheClearingOrchestration completed for schedule ID: {ScheduleId}", scheduleId);

            return "ok";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in CacheClearingOrchestration for schedule ID: {ScheduleId}", scheduleId);
            throw;
        }
    }
}