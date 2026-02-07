// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// This sample demonstrates how to configure OpenTelemetry distributed tracing with the Durable Task SDK.
// Traces are exported to Jaeger via OTLP, allowing you to visualize orchestration execution in the Jaeger UI.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Read the DTS emulator connection string from configuration.
// Default: "Endpoint=http://localhost:8080;Authentication=None;TaskHub=default"
string connectionString = builder.Configuration.GetValue<string>("DURABLE_TASK_SCHEDULER_CONNECTION_STRING")
    ?? "Endpoint=http://localhost:8080;Authentication=None;TaskHub=default";

// Configure OpenTelemetry tracing.
// The Durable Task SDK automatically emits traces using the "Microsoft.DurableTask" ActivitySource.
// We subscribe to that source and export traces to Jaeger via OTLP.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("DistributedTracingSample"))
    .WithTracing(tracing =>
    {
        tracing.AddSource("Microsoft.DurableTask");
        tracing.AddOtlpExporter();
    });

// Configure the Durable Task worker with a fan-out/fan-in orchestration and activities.
builder.Services.AddDurableTaskWorker(workerBuilder =>
{
    workerBuilder.AddTasks(tasks =>
    {
        tasks.AddOrchestratorFunc("FanOutFanIn", async context =>
        {
            // Fan-out: schedule multiple activity calls in parallel.
            string[] cities = ["Tokyo", "London", "Seattle", "Paris", "Sydney"];
            List<Task<string>> parallelTasks = new();
            foreach (string city in cities)
            {
                parallelTasks.Add(context.CallActivityAsync<string>("GetWeather", city));
            }

            // Fan-in: wait for all activities to complete.
            string[] results = await Task.WhenAll(parallelTasks);

            // Aggregate results in a final activity.
            string summary = await context.CallActivityAsync<string>("CreateSummary", results);
            return summary;
        });

        tasks.AddActivityFunc<string, string>("GetWeather", (context, city) =>
        {
            // Simulate fetching weather data for a city.
            string[] conditions = ["Sunny", "Cloudy", "Rainy", "Snowy", "Windy"];
            string condition = conditions[Math.Abs(city.GetHashCode()) % conditions.Length];
            int temperature = 15 + (Math.Abs(city.GetHashCode()) % 20);
            return $"{city}: {condition}, {temperature}°C";
        });

        tasks.AddActivityFunc<string[], string>("CreateSummary", (context, forecasts) =>
        {
            return $"Weather report for {forecasts.Length} cities:\n" + string.Join("\n", forecasts);
        });
    });

    workerBuilder.UseDurableTaskScheduler(connectionString);
});

// Configure the Durable Task client to connect to the DTS emulator.
builder.Services.AddDurableTaskClient(clientBuilder =>
{
    clientBuilder.UseDurableTaskScheduler(connectionString);
});

IHost host = builder.Build();
await host.StartAsync();

// Schedule the orchestration and wait for it to complete.
await using DurableTaskClient client = host.Services.GetRequiredService<DurableTaskClient>();
string instanceId = await client.ScheduleNewOrchestrationInstanceAsync("FanOutFanIn");
Console.WriteLine($"Started orchestration instance: '{instanceId}'");

OrchestrationMetadata result = await client.WaitForInstanceCompletionAsync(
    instanceId,
    getInputsAndOutputs: true,
    new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);

Console.WriteLine($"Orchestration completed with status: {result.RuntimeStatus}");
Console.WriteLine($"Output:\n{result.ReadOutputAs<string>()}");
Console.WriteLine();
Console.WriteLine("View traces in Jaeger UI at http://localhost:16686");
Console.WriteLine("View orchestrations in DTS Emulator dashboard at http://localhost:8082");
Console.WriteLine("Look for service 'DistributedTracingSample' to see the orchestration trace.");

await host.StopAsync();
