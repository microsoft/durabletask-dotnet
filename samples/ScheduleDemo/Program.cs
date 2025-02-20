// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.ScheduledTasks;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Create the host builder
IHost host = Host.CreateDefaultBuilder(args)
            .ConfigureServices(services =>
            {
                string connectionString = Environment.GetEnvironmentVariable("DURABLE_TASK_SCHEDULER_CONNECTION_STRING")
                    ?? throw new InvalidOperationException("Missing required environment variable 'DURABLE_TASK_SCHEDULER_CONNECTION_STRING'");

                // Configure the worker
                services.AddDurableTaskWorker(builder =>
                {
                    // Add the Schedule entity and demo orchestration
                    builder.AddTasks(r =>
                    {
                        // Add a demo orchestration that will be triggered by the schedule
                        r.AddOrchestratorFunc("DemoOrchestration", async context =>
                        {
                            string input = context.GetInput<string>();
                            await context.CallActivityAsync("ProcessMessage", input);
                            return $"Completed processing at {DateTime.UtcNow}";
                        });
                        // Add a demo activity
                        r.AddActivityFunc<string, string>("ProcessMessage", (context, message) => $"Processing message: {message}");
                    });

                    // Enable scheduled tasks support
                    builder.EnableScheduledTasksSupport();
                    builder.UseDurableTaskScheduler(connectionString);
                });

                // Configure the client
                services.AddDurableTaskClient(builder =>
                {
                    builder.EnableScheduledTasksSupport();
                    builder.UseDurableTaskScheduler(connectionString);
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
    // Create schedule options that runs every 30 seconds
    var scheduleOptions = new ScheduleCreationOptions("DemoOrchestration")
    {
        ScheduleId = "demo-schedule",
        Interval = TimeSpan.FromSeconds(30),
        StartAt = DateTimeOffset.UtcNow,
        OrchestrationInput = "This is a scheduled message!"
    };

    // Create the schedule
    Console.WriteLine("Creating schedule...");
    IScheduleHandle scheduleHandle = await scheduledTaskClient.CreateScheduleAsync(scheduleOptions);
    Console.WriteLine($"Created schedule with ID: {scheduleHandle.ScheduleId}");

    // Monitor the schedule for a while
    Console.WriteLine("\nMonitoring schedule for 2 minutes...");
    for (int i = 0; i < 4; i++)
    {
        await Task.Delay(TimeSpan.FromSeconds(30));
        var scheduleDescription = await scheduleHandle.DescribeAsync();
        Console.WriteLine($"\nSchedule status: {scheduleDescription.Status}");
        Console.WriteLine($"Last run at: {scheduleDescription.LastRunAt}");
        Console.WriteLine($"Next run at: {scheduleDescription.NextRunAt}");
    }

    // Pause the schedule
    Console.WriteLine("\nPausing schedule...");
    await scheduleHandle.PauseAsync();

    var pausedSchedule = await scheduleHandle.DescribeAsync();
    Console.WriteLine($"Schedule status after pause: {pausedSchedule.Status}");

    // Resume the schedule
    Console.WriteLine("\nResuming schedule...");
    await scheduleHandle.ResumeAsync();

    var resumedSchedule = await scheduleHandle.DescribeAsync();
    Console.WriteLine($"Schedule status after resume: {resumedSchedule.Status}");
    Console.WriteLine($"Next run at: {resumedSchedule.NextRunAt}");

    Console.WriteLine("\nPress any key to delete the schedule and exit...");
    Console.ReadKey();

    // Delete the schedule
    await scheduleHandle.DeleteAsync();
    Console.WriteLine("Schedule deleted.");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}

await host.StopAsync();