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

            // Create a large payload of approximately 10K
            StringBuilder largePayload = new StringBuilder(10240);
            for (int i = 0; i < 2; i++)
            {
                largePayload.Append($"Data chunk {i}: This is a large payload for testing orchestration with big data. ");
            }
            
            string bigData = largePayload.ToString();
            logger.LogInformation("Created large payload of size: {Size} bytes", bigData.Length);

            return bigData;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in CacheClearingOrchestration for schedule ID: {ScheduleId}", scheduleId);
            throw;
        }
    }
}