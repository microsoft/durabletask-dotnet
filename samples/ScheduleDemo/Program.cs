﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.ScheduledTasks;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScheduleDemo.Activities;


// Create the host builder
IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        string connectionString = Environment.GetEnvironmentVariable("DURABLE_TASK_SCHEDULER_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Missing required environment variable 'DURABLE_TASK_SCHEDULER_CONNECTION_STRING'");

        // Configure the worker
        _ = services.AddDurableTaskWorker(builder =>
        {
            // Add the Schedule entity and demo orchestration
            builder.AddTasks(r =>
            {
                // Add the orchestrator class
                r.AddOrchestrator<StockPriceOrchestrator>();

                // Add required activities
                r.AddActivity<GetStockPrice>();
            });

            // Enable scheduled tasks support
            builder.UseDurableTaskScheduler(connectionString);
            builder.EnableScheduledTasksSupport();
        });

        // Configure the client
        services.AddDurableTaskClient(builder =>
        {
            builder.UseDurableTaskScheduler(connectionString);
            builder.EnableScheduledTasksSupport();
        });

        // Configure console logging
        services.AddLogging(logging =>
        {
            logging.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.UseUtcTimestamp = true;
                options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
            });
        });
    })
    .Build();

await host.StartAsync();

IScheduledTaskClient scheduledTaskClient = host.Services.GetRequiredService<IScheduledTaskClient>();

try
{
    // list all schedules
    // Define the initial query with the desired page size
    ScheduleQuery query = new ScheduleQuery { PageSize = 100 };

    // Retrieve the pageable collection of schedule IDs
    AsyncPageable<string> schedules = await scheduledTaskClient.ListScheduleIdsAsync(query);

    // Initialize the continuation token
    string? continuationToken = null;
    await foreach (Page<string> page in schedules.AsPages(continuationToken))
    {
        foreach (string scheduleId in page.Values)
        {
            // Obtain the schedule handle for the current scheduleId
            IScheduleHandle handle = scheduledTaskClient.GetScheduleHandle(scheduleId);

            // Delete the schedule
            await handle.DeleteAsync();

            Console.WriteLine($"Deleted schedule {scheduleId}");
        }

        // Update the continuation token for the next iteration
        continuationToken = page.ContinuationToken;

        // If there's no continuation token, we've reached the end of the collection
        if (continuationToken == null)
        {
            break;
        }
    }


    // Create schedule options that runs every 4 seconds
    ScheduleCreationOptions scheduleOptions = new ScheduleCreationOptions("demo-schedule101", nameof(StockPriceOrchestrator), TimeSpan.FromSeconds(4))
    {
        StartAt = DateTimeOffset.UtcNow,
        OrchestrationInput = "MSFT"
    };

    // Create the schedule and get a handle to it
    ScheduleHandle scheduleHandle = await scheduledTaskClient.CreateScheduleAsync(scheduleOptions);

    // Get the schedule description
    ScheduleDescription scheduleDescription = await scheduleHandle.DescribeAsync();

    // print the schedule description
    Console.WriteLine(scheduleDescription.ToJsonString(true));

    Console.WriteLine("");
    Console.WriteLine("");
    Console.WriteLine("");

    // Pause the schedule
    Console.WriteLine("\nPausing schedule...");
    await scheduleHandle.PauseAsync();
    scheduleDescription = await scheduleHandle.DescribeAsync();
    Console.WriteLine(scheduleDescription.ToJsonString(true));
    Console.WriteLine("");
    Console.WriteLine("");
    Console.WriteLine("");


    // Resume the schedule
    Console.WriteLine("\nResuming schedule...");
    await scheduleHandle.ResumeAsync();
    scheduleDescription = await scheduleHandle.DescribeAsync();
    Console.WriteLine(scheduleDescription.ToJsonString(true));

    Console.WriteLine("");
    Console.WriteLine("");
    Console.WriteLine("");

    await Task.Delay(TimeSpan.FromMinutes(30));
    //Console.WriteLine("\nPress any key to delete the schedule and exit...");
    //Console.ReadKey();
}
catch (Exception ex)
{
    Console.WriteLine($"One of your schedule operations failed, please fix and rerun: {ex.Message}");
}

await host.StopAsync();