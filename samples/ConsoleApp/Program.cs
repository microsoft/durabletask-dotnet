// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddDurableTaskClient(builder =>
        {
            // Configure options for this builder. Can be omitted if no options customization is needed.
            builder.Configure(opt => { });
            builder.UseGrpc(); // multiple overloads available for providing gRPC information

            // AddDurableTaskClient allows for multiple named clients by passing in a name as the first argument.
            // When using a non-default named client, you will need to make this call below to have the
            // DurableTaskClient added directly to the DI container. Otherwise IDurableTaskClientProvider must be used
            // to retrieve DurableTaskClients by name from the DI container. In this case, we are using the default
            // name, so the line below is NOT required as it was already called for us.
            builder.RegisterDirectly();
        });

        services.AddDurableTaskWorker(builder =>
        {
            // Configure options for this builder. Can be omitted if no options customization is needed.
            builder.Configure(opt => { });

            // Register orchestrators and activities.
            builder.AddTasks(tasks =>
            {
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
            });

            builder.UseGrpc(); // multiple overloads available for providing gRPC information
        });

        // Can also configure worker and client options through all the existing options config methods.
        // These are equivalent to the 'builder.Configure' calls above.
        services.Configure<DurableTaskWorkerOptions>(opt => { });
        services.Configure<DurableTaskClientOptions>(opt => { });

        // Registry can also be done via options pattern. This is equivalent to the 'builder.AddTasks' call above.
        // You can use all the tools options pattern has available. For example, if you have multiple workers you could
        // use ConfigureAll<DurableTaskRegistry> to add tasks to ALL workers in one go. Otherwise, you need to use
        // named option configuration to register to specific workers (where the name matches the name passed to 
        // 'AddDurableTaskWorker(name?, builder)').
        services.Configure<DurableTaskRegistry>(registry => { });

        // You can configure custom data converter multiple ways. One is through worker/client options configuration.
        // Alternatively, data converter will be used from the service provider if available (as a singleton) AND no
        // converter was explicitly set on the options.
        services.AddSingleton<DataConverter>(JsonDataConverter.Default);
    })
    .Build();

await host.StartAsync();

await using DurableTaskClient client = host.Services.GetRequiredService<DurableTaskClient>();
string instanceId = await client.ScheduleNewOrchestrationInstanceAsync("HelloSequence");
Console.WriteLine($"Created instance: '{instanceId}'");

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1000));
OrchestrationMetadata instance = await client.WaitForInstanceCompletionAsync(
    instanceId,
    cts.Token,
    getInputsAndOutputs: true);

Console.WriteLine($"Instance completed: {instance}");