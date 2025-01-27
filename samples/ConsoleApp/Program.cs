// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

IDurableTaskClientBuilder clientBuilder = builder.Services.AddDurableTaskClient()
    .Configure(opt => { }) // configure options for this builder, if desired.
    .UseGrpc(); // multiple overloads available for providing gRPC information

// OPTIONAL STEP
// AddDurableTaskClient allows for multiple named clients by passing in a name as the first argument.
// When using a non-default named client, you will need to make this call below to have the
// DurableTaskClient added directly to the DI container. Otherwise IDurableTaskClientProvider must be used
// to retrieve DurableTaskClients by name from the DI container. In this case, we are using the default
// name, so the line below is NOT required as it was already called for us.
clientBuilder.RegisterDirectly();

builder.Services.AddDurableTaskWorker()
    .Configure(opt => { }) // configure options for this builder.
    .AddTasks(tasks =>
    {
        // Add tasks to the worker.
        tasks.AddOrchestratorFunc("HelloSequence", async context =>
        {
            var greetings = new List<string>
            {
                await context.CallActivityAsync<string>("SayHello", "Tokyo"),
                await context.CallActivityAsync<string>("SayHello", "London"),
                await context.CallActivityAsync<string>("SayHello", "Seattle"),
            };

            return greetings;
        });

        tasks.AddActivityFunc<string, string>("SayHello", (context, city) => $"Hello {city}!");
    })
    .UseGrpc(); // multiple overloads available for providing gRPC information

// OPTIONAL STEP
// Client and Worker options can also be configured through the options pattern.
// When using the options pattern, configure with the same name as the builder.
builder.Services.Configure<DurableTaskClientOptions>(opt => { });
builder.Services.Configure<DurableTaskWorkerOptions>(opt => { });
builder.Services.Configure<DurableTaskRegistry>(registry => { });

// OPTIONAL STEP
// You can configure custom data converter multiple ways. One is through worker/client options configuration.
// Alternatively, data converter will be used from the service provider if available (as a singleton) AND no
// converter was explicitly set on the options.
builder.Services.AddSingleton<DataConverter>(JsonDataConverter.Default);

IHost host = builder.Build();
await host.StartAsync();

await using DurableTaskClient client = host.Services.GetRequiredService<DurableTaskClient>();
string instanceId = await client.ScheduleNewOrchestrationInstanceAsync("HelloSequence");
Console.WriteLine($"Created instance: '{instanceId}'");

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1000));
OrchestrationMetadata instance = await client.WaitForInstanceCompletionAsync(
    instanceId,
    getInputsAndOutputs: true,
    cts.Token);

Console.WriteLine($"Instance completed: {instance}");
