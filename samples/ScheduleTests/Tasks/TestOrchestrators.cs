using Microsoft.DurableTask;
using Microsoft.DurableTask.ScheduledTasks;
using System;
using System.Threading.Tasks;

namespace ScheduleTests.Tasks
{
    [DurableTask]
    public class SimpleScheduleOrchestrator
    {
        public async Task<string> RunAsync(TaskOrchestrationContext context, DateTime scheduleTime)
        {
            await context.ScheduleTask(scheduleTime);
            return await context.CallActivityAsync<string>(nameof(TestActivity), "SimpleSchedule");
        }
    }

    [DurableTask]
    public class RecurringScheduleOrchestrator
    {
        public async Task<string> RunAsync(TaskOrchestrationContext context, DateTime startTime)
        {
            // Schedule task to run every minute for 3 times
            for (int i = 0; i < 3; i++)
            {
                DateTime nextRun = startTime.AddMinutes(i);
                await context.ScheduleTask(nextRun);
                await context.CallActivityAsync<string>(nameof(TestActivity), $"RecurringSchedule-{i}");
            }
            return "Completed recurring schedule";
        }
    }

    [DurableTask]
    public class CronScheduleOrchestrator
    {
        public async Task<string> RunAsync(TaskOrchestrationContext context, DateTime startTime)
        {
            // Schedule using CRON expression (every minute)
            await context.ScheduleWithCron("* * * * *", startTime);
            return await context.CallActivityAsync<string>(nameof(TestActivity), "CronSchedule");
        }
    }
} 