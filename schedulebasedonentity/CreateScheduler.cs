using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.DurableTask.Client.Entities;

public static class CreateSchedule
{
    [FunctionName("CreateSchedule")]
    public static async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req,
        [DurableClient] IDurableEntityClient client)
    {
        string scheduleName = await new StreamReader(req.Body).ReadToEndAsync();
        string cronExpression = req.Query["cron"];
        string orchestrationName = req.Query["orchestration"];

        if (string.IsNullOrEmpty(scheduleName) || string.IsNullOrEmpty(cronExpression) || string.IsNullOrEmpty(orchestrationName))
        {
            return new BadRequestObjectResult("Schedule name, cron expression and orchestration name are required");
        }

        var schedulerId = new EntityInstanceId("SchedulerEntity", scheduleName);

        // Create the schedule
        await client.SignalEntityAsync(schedulerId, "CreateSchedule", new
        {
            ScheduleName = scheduleName,
            CronExpression = cronExpression,
            OrchestrationName = orchestrationName
        });

        return new OkObjectResult($"Schedule '{scheduleName}' has been created.");
    }
}
