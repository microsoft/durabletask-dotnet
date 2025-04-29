// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using ScheduleWebApp.Activities;
using System.Text;

namespace ScheduleWebApp.Orchestrations;

public class CacheClearingOrchestrator : TaskOrchestrator<string, string>
{
    private static readonly Random Random = new();

    public override async Task<string> RunAsync(TaskOrchestrationContext context, string scheduleId)
    {
        ILogger logger = context.CreateReplaySafeLogger(nameof(CacheClearingOrchestrator));
        try
        {
            logger.LogInformation("Starting CacheClearingOrchestration for schedule ID: {ScheduleId}", scheduleId);

            // // 50% chance of failure
            // if (Random.NextDouble() < 0.5)
            // {
            //     throw new Exception("Random failure in CacheClearingOrchestration");
            // }
            
            // Get current stock price
            decimal currentPrice = await context.CallGetStockPriceAsync("MSFT");

            // 10kb
            return new string('X', 102400);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in CacheClearingOrchestration for schedule ID: {ScheduleId}", scheduleId);
            throw;
        }
    }
}