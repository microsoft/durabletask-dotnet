using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;

public static class GenerateDailyReport
{
    [FunctionName("GenerateDailyReport")]
    public static async Task Run([OrchestrationTrigger] IDurableOrchestrationContext context)
    {
        string reportDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
        Console.WriteLine($"Generating daily financial report for {reportDate}");

        // Simulate work
        await Task.Delay(1000);
    }
}
