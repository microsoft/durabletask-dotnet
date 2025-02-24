// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.ScheduledTasks;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Get configuration
string connectionString = builder.Configuration.GetValue<string>("DURABLE_TASK_SCHEDULER_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Missing required configuration 'DURABLE_TASK_SCHEDULER_CONNECTION_STRING'");

// Configure the worker
builder.Services.AddDurableTaskWorker(builder =>
{
    // Add the Schedule entity and demo orchestration
    builder.AddTasks(r => r.AddAllGeneratedTasks());

    // Enable scheduled tasks support
    builder.UseDurableTaskScheduler(connectionString);
    builder.UseScheduledTasks();
});

// Configure the client
builder.Services.AddDurableTaskClient(builder =>
{
    builder.UseDurableTaskScheduler(connectionString);
    builder.UseScheduledTasks();
});

// Configure console logging
builder.Services.AddLogging(logging =>
{
    logging.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.UseUtcTimestamp = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
    });
});

IHost host = builder.Build();
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
    await foreach (string scheduleId in schedules)
    {
        // Obtain the schedule handle for the current scheduleId
        IScheduleHandle handle = scheduledTaskClient.GetScheduleHandle(scheduleId);

        // Delete the schedule
        await handle.DeleteAsync();

        Console.WriteLine($"Deleted schedule {scheduleId}");
    }

    // Create schedule options that runs every 4 seconds
    ScheduleCreationOptions scheduleOptions = new ScheduleCreationOptions("demo-schedule101", nameof(StockPriceOrchestrator), TimeSpan.FromSeconds(4))
    {
        StartAt = DateTimeOffset.UtcNow,
        OrchestrationInput = "MSFT"
    };

    // Create the schedule and get a handle to it
    IScheduleHandle scheduleHandle = await scheduledTaskClient.CreateScheduleAsync(scheduleOptions);

    // Get the schedule description
    ScheduleDescription scheduleDescription = await scheduleHandle.DescribeAsync();

    // print the schedule description
    Console.WriteLine(scheduleDescription);

    Console.WriteLine("");
    Console.WriteLine("");
    Console.WriteLine("");

    // Pause the schedule
    Console.WriteLine("\nPausing schedule...");
    await scheduleHandle.PauseAsync();
    scheduleDescription = await scheduleHandle.DescribeAsync();
    Console.WriteLine(scheduleDescription);
    Console.WriteLine("");
    Console.WriteLine("");
    Console.WriteLine("");

    // Resume the schedule
    Console.WriteLine("\nResuming schedule...");
    await scheduleHandle.ResumeAsync();
    scheduleDescription = await scheduleHandle.DescribeAsync();
    Console.WriteLine(scheduleDescription);

    Console.WriteLine("");
    Console.WriteLine("");
    Console.WriteLine("");

    await Task.Delay(TimeSpan.FromMinutes(30));
}
catch (Exception ex)
{
    Console.WriteLine($"One of your schedule operations failed, please fix and rerun: {ex.Message}");
}

await host.StopAsync();