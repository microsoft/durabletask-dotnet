// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DurableTask.Abstractions.Entities.Schedule;

internal class Program
{
    private static async Task Main(string[] args)
    {
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
                        r.AddAllGeneratedTasks();
                        r.AddEntity<Schedule>();
                        
                        // Add a demo orchestration that will be triggered by the schedule
                        r.AddOrchestratorFunc("DemoOrchestration", async TaskOrchestrationContext context =>
                        {
                            string input = context.GetInput<string>();
                            await context.CallActivityAsync("ProcessMessage", input);
                            return $"Completed processing at {DateTime.UtcNow}";
                        });

                        // Add a demo activity
                        r.AddActivityFunc("ProcessMessage", (TaskActivityContext context, string message) =>
                        {
                            context.GetLogger().LogInformation($"Processing scheduled message: {message}");
                            return Task.CompletedTask;
                        });
                    });
                    builder.UseDurableTaskScheduler(connectionString);
                });

                // Configure the client
                services.AddDurableTaskClient(builder =>
                {
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
        await using DurableTaskClient client = host.Services.GetRequiredService<DurableTaskClient>();

        try
        {
            // Create a schedule that runs every 30 seconds
            ScheduleConfiguration scheduleConfig = new ScheduleConfiguration(
                orchestrationName: "DemoOrchestration",
                scheduleId: "demo-schedule")
            {
                Interval = TimeSpan.FromSeconds(30),
                StartAt = DateTimeOffset.UtcNow,
                OrchestrationInput = "This is a scheduled message!"
            };

            // Create the schedule
            Console.WriteLine("Creating schedule...");
            await client.CreateScheduleAsync(scheduleConfig);
            Console.WriteLine($"Created schedule with ID: {scheduleConfig.ScheduleId}");

            // Monitor the schedule for a while
            Console.WriteLine("\nMonitoring schedule for 2 minutes...");
            for (int i = 0; i < 4; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(30));
                Schedule schedule = await client.GetScheduleAsync(scheduleConfig.ScheduleId);
                Console.WriteLine($"\nSchedule status: {schedule.Status}");
                Console.WriteLine($"Last run at: {schedule.LastRunAt}");
                Console.WriteLine($"Next run at: {schedule.NextRunAt}");
            }

            // Pause the schedule
            Console.WriteLine("\nPausing schedule...");
            await client.PauseScheduleAsync(scheduleConfig.ScheduleId);
            
            Schedule pausedSchedule = await client.GetScheduleAsync(scheduleConfig.ScheduleId);
            Console.WriteLine($"Schedule status after pause: {pausedSchedule.Status}");

            // Resume the schedule
            Console.WriteLine("\nResuming schedule...");
            await client.ResumeScheduleAsync(scheduleConfig.ScheduleId);
            
            Schedule resumedSchedule = await client.GetScheduleAsync(scheduleConfig.ScheduleId);
            Console.WriteLine($"Schedule status after resume: {resumedSchedule.Status}");
            Console.WriteLine($"Next run at: {resumedSchedule.NextRunAt}");

            Console.WriteLine("\nPress any key to delete the schedule and exit...");
            Console.ReadKey();

            // Delete the schedule
            await client.DeleteScheduleAsync(scheduleConfig.ScheduleId);
            Console.WriteLine("Schedule deleted.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }

        await host.StopAsync();
    }
}
