// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Dapr.DurableTask.Client;
using Dapr.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NetFxConsoleApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            IHost host = Host.CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                {
                    services.AddDurableTaskClient(builder => builder.UseGrpc());
                    services.AddDurableTaskWorker(builder =>
                    {
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

                        builder.UseGrpc();
                    });
                })
                .Build();

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
        }
    }
}
