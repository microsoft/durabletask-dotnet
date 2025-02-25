// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;

namespace ScheduleWebApp.Orchestrations;

public class CacheClearingOrchestrator : TaskOrchestrator<string, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, string scheduleId)
    {
        ILogger logger = context.CreateReplaySafeLogger(nameof(CacheClearingOrchestrator));
        try
        {
            logger.LogInformation("Starting CacheClearingOrchestration for schedule ID: {ScheduleId}", scheduleId);

            // Simulate cache clearing
            await Task.Delay(TimeSpan.FromSeconds(5));

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