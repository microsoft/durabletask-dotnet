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
using ScheduleConsoleApp;

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

// Run the schedule operations
ScheduledTaskClient scheduledTaskClient = host.Services.GetRequiredService<ScheduledTaskClient>();
ScheduleOperations scheduleOperations = new ScheduleOperations(scheduledTaskClient);
await scheduleOperations.RunAsync();

await host.StopAsync();