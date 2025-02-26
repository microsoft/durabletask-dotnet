// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using Microsoft.DurableTask.ScheduledTasks;

namespace ScheduleConsoleApp;

class ScheduleOperations(ScheduledTaskClient scheduledTaskClient)
{
    readonly ScheduledTaskClient scheduledTaskClient = scheduledTaskClient ?? throw new ArgumentNullException(nameof(scheduledTaskClient));

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
            ScheduleClient scheduleClient1 = this.scheduledTaskClient.GetScheduleClient(schedule.ScheduleId);
            await scheduleClient1.DeleteAsync();
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
        ScheduleClient scheduleClient = await this.scheduledTaskClient.CreateScheduleAsync(scheduleOptions);

        // Get and print the initial schedule description
        await PrintScheduleDescriptionAsync(scheduleClient);

        // Pause the schedule
        Console.WriteLine("\nPausing schedule...");
        await scheduleClient.PauseAsync();
        await PrintScheduleDescriptionAsync(scheduleClient);

        // Resume the schedule
        Console.WriteLine("\nResuming schedule...");
        await scheduleClient.ResumeAsync();
        await PrintScheduleDescriptionAsync(scheduleClient);

        // Wait for a while to let the schedule run
        await Task.Delay(TimeSpan.FromMinutes(30));
    }

    static async Task PrintScheduleDescriptionAsync(ScheduleClient scheduleClient)
    {
        ScheduleDescription scheduleDescription = await scheduleClient.DescribeAsync();
        Console.WriteLine(scheduleDescription);
        Console.WriteLine("\n\n");
    }
}