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
            IsEnabled = true
        };

        // Signal the entity to run the schedule immediately
        ctx.SignalEntity(ctx.EntityId, "RunSchedule");
    }

    public void UpdateSchedule(string scheduleName, string cronExpression, string orchestrationName)
    {
        if (this.schedule == null)
        {
            throw new InvalidOperationException("Schedule does not exist.");
        }

        this.schedule.Name = scheduleName;
        this.schedule.CronExpression = cronExpression;
        this.schedule.OrchestrationName = orchestrationName;
        this.schedule.NextRun = DateTime.UtcNow; // Reset next run to now after update
    }

    public ScheduleMetadata GetSchedule()
    {
        if (this.schedule == null)
        {
            throw new InvalidOperationException("Schedule does not exist.");
        }

        return this.schedule;
    }

    public void EnableSchedule()
    {
        if (this.schedule == null)
        {
            throw new InvalidOperationException("Schedule does not exist.");
        }

        this.schedule.IsEnabled = true;
        this.schedule.NextRun = DateTime.UtcNow; // Start immediately when enabled
        ctx.SignalEntity(ctx.EntityId, "RunSchedule");
    }

    public void DisableSchedule()
    {
        if (this.schedule == null)
        {
            throw new InvalidOperationException("Schedule does not exist.");
        }

        this.schedule.IsEnabled = false;
    }

    async Task TriggerOrchestration(IDurableEntityContext ctx)
    {
        string instanceId = await ctx.CallOrchestratorAsync("GenerateDailyReport");
        ctx.SetState(this); // Save entity state
    }

    public async Task RunSchedule(IDurableEntityContext ctx)
    {
        if (schedule == null)
        {
            throw new InvalidOperationException("Schedule not created.");
        }

        if (!schedule.IsEnabled)
        {
            return; // Don't run if schedule is disabled
        }

        // Wait until the next scheduled time
        TimeSpan delay = schedule.NextRun - DateTime.UtcNow;
        if (delay > TimeSpan.Zero)
        {
            await ctx.CreateTimer(schedule.NextRun, CancellationToken.None);
        }

        // Trigger the target orchestration
        await TriggerOrchestration(ctx);

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
    public bool IsEnabled { get; set; }
}
