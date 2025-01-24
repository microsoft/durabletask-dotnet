using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.DurableTask.Client.Entities;
using System.IO;
using System;

public static class SchedulerEndpoints
{
    [FunctionName("CreateSchedule")]
    public static async Task<IActionResult> CreateSchedule(
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

    [FunctionName("UpdateSchedule")]
    public static async Task<IActionResult> UpdateSchedule(
        [HttpTrigger(AuthorizationLevel.Function, "put")] HttpRequest req,
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

        // Update the schedule
        await client.SignalEntityAsync(schedulerId, "UpdateSchedule", new
        {
            ScheduleName = scheduleName,
            CronExpression = cronExpression,
            OrchestrationName = orchestrationName
        });

        return new OkObjectResult($"Schedule '{scheduleName}' has been updated.");
    }

    [FunctionName("GetSchedule")]
    public static async Task<IActionResult> GetSchedule(
        [HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequest req,
        [DurableClient] IDurableEntityClient client)
    {
        string scheduleName = req.Query["name"];

        if (string.IsNullOrEmpty(scheduleName))
        {
            return new BadRequestObjectResult("Schedule name is required");
        }

        var schedulerId = new EntityInstanceId("SchedulerEntity", scheduleName);
        var state = await client.ReadEntityStateAsync<SchedulerEntity>(schedulerId);

        if (!state.EntityExists)
        {
            return new NotFoundObjectResult($"Schedule '{scheduleName}' not found.");
        }

        var schedule = state.EntityState.GetSchedule();
        return new OkObjectResult(schedule);
    }

    [FunctionName("EnableSchedule")]
    public static async Task<IActionResult> EnableSchedule(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req,
        [DurableClient] IDurableEntityClient client)
    {
        string scheduleName = req.Query["name"];

        if (string.IsNullOrEmpty(scheduleName))
        {
            return new BadRequestObjectResult("Schedule name is required");
        }

        var schedulerId = new EntityInstanceId("SchedulerEntity", scheduleName);
        await client.SignalEntityAsync(schedulerId, "EnableSchedule");

        return new OkObjectResult($"Schedule '{scheduleName}' has been enabled.");
    }

    [FunctionName("DisableSchedule")]
    public static async Task<IActionResult> DisableSchedule(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req,
        [DurableClient] IDurableEntityClient client)
    {
        string scheduleName = req.Query["name"];

        if (string.IsNullOrEmpty(scheduleName))
        {
            return new BadRequestObjectResult("Schedule name is required");
        }

        var schedulerId = new EntityInstanceId("SchedulerEntity", scheduleName);
        await client.SignalEntityAsync(schedulerId, "DisableSchedule");

        return new OkObjectResult($"Schedule '{scheduleName}' has been disabled.");
    }
}
