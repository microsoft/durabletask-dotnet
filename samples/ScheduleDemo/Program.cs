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
        _ = services.AddDurableTaskWorker(builder =>
        {
            // Add the Schedule entity and demo orchestration
            builder.AddTasks(r =>
            {
                // Add a demo orchestration that will be triggered by the schedule
                r.AddOrchestratorFunc("DemoOrchestration", async context =>
                {
                    string stockSymbol = "MSFT"; // Default to MSFT if no input provided
                    var logger = context.CreateReplaySafeLogger("DemoOrchestration");
                    logger.LogInformation("Getting stock price for: {symbol}", stockSymbol);

                    try
                    {
                        // Get current stock price
                        decimal currentPrice = await context.CallActivityAsync<decimal>("GetStockPrice", stockSymbol);

                        logger.LogInformation("Current price for {symbol} is ${price:F2}", stockSymbol, currentPrice);

                        return $"Stock {stockSymbol} price: ${currentPrice:F2} at {DateTime.UtcNow}";
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error processing stock price for {symbol}", stockSymbol);
                        throw;
                    }
                });

                // Add required activities
                r.AddActivityFunc<string, decimal>("GetStockPrice", (context, symbol) =>
                {
                    // Mock implementation - would normally call stock API
                    return 100.00m;
                });
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
    // TODO: ORCHname 
    // Create schedule options that runs every 30 seconds
    ScheduleCreationOptions scheduleOptions = new ScheduleCreationOptions
    {
        OrchestrationName = "DemoOrchestration",
        ScheduleId = "demo-schedule14",
        Interval = TimeSpan.FromSeconds(4),
        StartAt = DateTimeOffset.UtcNow,
        OrchestrationInput = "MSFT"
    };

    // Get schedule handle
    IScheduleHandle scheduleHandle = scheduledTaskClient.GetScheduleHandle(scheduleOptions.ScheduleId);

    // Create the schedule
    Console.WriteLine("Creating schedule...");
    IScheduleWaiter waiter = await scheduleHandle.CreateAsync(scheduleOptions);
    ScheduleDescription scheduleDescription = await waiter.WaitUntilActiveAsync();
    // print the schedule description
    Console.WriteLine(scheduleDescription.ToJsonString(true));

    Console.WriteLine("");
    Console.WriteLine("");
    Console.WriteLine("");

    // Pause the schedule
    Console.WriteLine("\nPausing schedule...");
    IScheduleWaiter pauseWaiter = await scheduleHandle.PauseAsync();
    scheduleDescription = await pauseWaiter.WaitUntilPausedAsync();
    Console.WriteLine(scheduleDescription.ToJsonString(true));
    Console.WriteLine("");
    Console.WriteLine("");
    Console.WriteLine("");


    // Resume the schedule
    Console.WriteLine("\nResuming schedule...");
    IScheduleWaiter resumeWaiter = await scheduleHandle.ResumeAsync();

    scheduleDescription = await resumeWaiter.WaitUntilActiveAsync();
    Console.WriteLine(scheduleDescription.ToJsonString(true));

    Console.WriteLine("");
    Console.WriteLine("");
    Console.WriteLine("");

    await Task.Delay(2000000);
    //Console.WriteLine("\nPress any key to delete the schedule and exit...");
    //Console.ReadKey();

    // Delete the schedule
    IScheduleWaiter deleteWaiter = await scheduleHandle.DeleteAsync();
    bool deleted = await deleteWaiter.WaitUntilDeletedAsync();
    Console.WriteLine(deleted ? "Schedule deleted." : "Schedule not deleted.");
}
catch (Exception ex)
{
    Console.WriteLine($"One of your schedule operations failed, please fix and rerun: {ex.Message}");
}

await host.StopAsync();