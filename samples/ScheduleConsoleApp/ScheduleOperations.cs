// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using Microsoft.DurableTask.ScheduledTasks;

namespace ScheduleConsoleApp;

class ScheduleOperations(IScheduledTaskClient scheduledTaskClient)
{
    readonly IScheduledTaskClient scheduledTaskClient = scheduledTaskClient ?? throw new ArgumentNullException(nameof(scheduledTaskClient));

    public async Task RunAsync()
    {
        try
        {
            await this.DeleteExistingSchedulesAsync();
            await this.CreateAndManageScheduleAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"One of your schedule operations failed, please fix and rerun: {ex.Message}");
        }
    }

    async Task DeleteExistingSchedulesAsync()
    {
        // Define the initial query with the desired page size
        ScheduleQuery query = new ScheduleQuery { PageSize = 100 };

        // Retrieve the pageable collection of schedule IDs
        AsyncPageable<ScheduleDescription> schedules = this.scheduledTaskClient.ListSchedulesAsync(query);

        // Delete each existing schedule
        await foreach (ScheduleDescription schedule in schedules)
        {
            ScheduleClient handle = this.scheduledTaskClient.GetDefaultScheduleClient(schedule.ScheduleId);
            await handle.DeleteAsync();
            Console.WriteLine($"Deleted schedule {schedule.ScheduleId}");
        }
    }

    async Task CreateAndManageScheduleAsync()
    {
        // Create schedule options that runs every 4 seconds
        ScheduleCreationOptions scheduleOptions = new ScheduleCreationOptions(
            "demo-schedule101",
            nameof(StockPriceOrchestrator),
            TimeSpan.FromSeconds(4))
        {
            StartAt = DateTimeOffset.UtcNow,
            OrchestrationInput = "MSFT"
        };

        // Create the schedule and get a handle to it
        ScheduleClient DefaultScheduleClient = await this.scheduledTaskClient.CreateScheduleAsync(scheduleOptions);

        // Get and print the initial schedule description
        await PrintScheduleDescriptionAsync(DefaultScheduleClient);

        // Pause the schedule
        Console.WriteLine("\nPausing schedule...");
        await DefaultScheduleClient.PauseAsync();
        await PrintScheduleDescriptionAsync(DefaultScheduleClient);

        // Resume the schedule
        Console.WriteLine("\nResuming schedule...");
        await DefaultScheduleClient.ResumeAsync();
        await PrintScheduleDescriptionAsync(DefaultScheduleClient);

        // Wait for a while to let the schedule run
        await Task.Delay(TimeSpan.FromMinutes(30));
    }

    static async Task PrintScheduleDescriptionAsync(ScheduleClient DefaultScheduleClient)
    {
        ScheduleDescription scheduleDescription = await DefaultScheduleClient.DescribeAsync();
        Console.WriteLine(scheduleDescription);
        Console.WriteLine("\n\n");
    }
}