// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Microsoft.DurableTask;
using Dapr.DurableTask.ScheduledTasks;
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

// Configure logging
builder.Services.AddSingleton<ILogger>(sp => sp.GetRequiredService<ILoggerFactory>().CreateLogger<Program>());
builder.Services.AddLogging();

IHost host = builder.Build();
await host.StartAsync();

// Run the schedule operations
ScheduledTaskClient scheduledTaskClient = host.Services.GetRequiredService<ScheduledTaskClient>();
await ScheduleDemo.RunDemoAsync(scheduledTaskClient);

await host.StopAsync();