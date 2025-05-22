// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using Dapr.DurableTask.ScheduledTasks;

namespace ScheduleConsoleApp;

/// <summary>
/// Demonstrates various schedule operations in a sample application.
/// </summary>
static class ScheduleDemo
{
    public static async Task RunDemoAsync(ScheduledTaskClient scheduledTaskClient)
    {
        ArgumentNullException.ThrowIfNull(scheduledTaskClient);

        try
        {
            await DeleteExistingSchedulesAsync(scheduledTaskClient);
            await CreateAndManageScheduleAsync(scheduledTaskClient);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"One of your schedule operations failed, please fix and rerun: {ex.Message}");
        }
    }

    static async Task DeleteExistingSchedulesAsync(ScheduledTaskClient scheduledTaskClient)
    {
        // Define the initial query with the desired page size
        ScheduleQuery query = new ScheduleQuery { PageSize = 100 };

        // Retrieve the pageable collection of schedule IDs
        AsyncPageable<ScheduleDescription> schedules = scheduledTaskClient.ListSchedulesAsync(query);

        // Delete each existing schedule
        await foreach (ScheduleDescription schedule in schedules)
        {
            ScheduleClient scheduleClient = scheduledTaskClient.GetScheduleClient(schedule.ScheduleId);
            await scheduleClient.DeleteAsync();
            Console.WriteLine($"Deleted schedule {schedule.ScheduleId}");
        }
    }

    static async Task CreateAndManageScheduleAsync(ScheduledTaskClient scheduledTaskClient)
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
        ScheduleClient scheduleClient = await scheduledTaskClient.CreateScheduleAsync(scheduleOptions);

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