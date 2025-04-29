// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;

namespace ScheduleWebApp.Activities;

[DurableTask]
public class GetStockPrice : TaskActivity<string, decimal>
{
    public override Task<decimal> RunAsync(TaskActivityContext context, string symbol)
    {
        // Mock implementation - would normally call stock API
        return Task.FromResult(100.00m);
    }
}
