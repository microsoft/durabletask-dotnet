// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using System.Text;

namespace ScheduleWebApp.Orchestrations;

public class CacheClearingOrchestrator : TaskOrchestrator<string, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, string scheduleId)
    {
        ILogger logger = context.CreateReplaySafeLogger(nameof(CacheClearingOrchestrator));
        try
        {
            logger.LogInformation("Starting CacheClearingOrchestration for schedule ID: {ScheduleId}", scheduleId);

            
            // 10kb
            return new string('X', 10240);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in CacheClearingOrchestration for schedule ID: {ScheduleId}", scheduleId);
            throw;
        }
    }
}