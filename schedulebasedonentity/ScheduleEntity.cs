using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cronos;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Newtonsoft.Json;

[JsonObject(MemberSerialization.OptIn)]
public class SchedulerEntity
{
    [JsonProperty("schedule")]
    private ScheduleMetadata? schedule;

    public void CreateSchedule(string scheduleName, string cronExpression, string orchestrationName)
    {
        if (this.schedule != null)
        {
            throw new InvalidOperationException("Schedule already exists.");
        }

        this.schedule = new ScheduleMetadata
        {
            Name = scheduleName,
            CronExpression = cronExpression,
            OrchestrationName = orchestrationName,
            NextRun = DateTime.UtcNow, // Set next run to now to run immediately
        };

        // Signal the entity to run the schedule immediately
        ctx.SignalEntity(ctx.EntityId, "RunSchedule");
    }

    public async Task RunSchedule(IDurableEntityContext ctx)
    {
        if (schedule == null)
        {
            throw new InvalidOperationException("Schedule not created.");
        }

        // Wait until the next scheduled time
        TimeSpan delay = schedule.NextRun - DateTime.UtcNow;
        if (delay > TimeSpan.Zero)
        {
            await ctx.CreateTimer(schedule.NextRun, CancellationToken.None);
        }

        // Trigger the target orchestration
        var instanceId = Guid.NewGuid().ToString();
        ctx.SignalEntity(new EntityId(schedule.OrchestrationName, instanceId), "Run");

        // Update the next run time
        schedule.NextRun = CronExpressionParser.GetNextOccurrence(schedule.CronExpression, DateTime.UtcNow);

        // Reschedule by signaling itself
        ctx.SignalEntity(ctx.EntityId, "RunSchedule");
    }

    [FunctionName(nameof(SchedulerEntity))]
    public static Task Run([EntityTrigger] IDurableEntityContext context)
    {
        return context.DispatchAsync<SchedulerEntity>();
    }
}

public class ScheduleMetadata
{
    public string Name { get; set; } = null!;
    public string CronExpression { get; set; } = null!;
    public string OrchestrationName { get; set; } = null!;
    public DateTime NextRun { get; set; }
}
