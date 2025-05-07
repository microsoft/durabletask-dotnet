// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using ScheduleWebApp.Activities;

namespace ScheduleWebApp.Orchestrations;

public class CacheClearingOrchestrator : TaskOrchestrator<string, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, string scheduleId)
    {
        ILogger logger = context.CreateReplaySafeLogger(nameof(CacheClearingOrchestrator));
        try
        {
            logger.LogInformation("Starting CacheClearingOrchestration for schedule ID: {ScheduleId}", scheduleId);

            CallActivityOptions options = new TaskOptions().WithTags(new Dictionary<string, string>
            {
                { "scheduleId", scheduleId }
            });

            await context.CallActivityAsync(nameof(CacheClearingActivity), options);

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