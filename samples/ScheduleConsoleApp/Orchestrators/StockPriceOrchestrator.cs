// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

[DurableTask]
public class StockPriceOrchestrator : TaskOrchestrator<string, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, string symbol)
    {
        var logger = context.CreateReplaySafeLogger("DemoOrchestration");
        logger.LogInformation("Getting stock price for: {symbol}", symbol);
        try
        {
            // Get current stock price
            decimal currentPrice = await context.CallGetStockPriceAsync(symbol);

            logger.LogInformation("Current price for {symbol} is ${price:F2}", symbol, currentPrice);

            return $"Stock {symbol} price: ${currentPrice:F2} at {context.CurrentUtcDateTime}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing stock price for {symbol}", symbol);
            throw;
        }
    }
}