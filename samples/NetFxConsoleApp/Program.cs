using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Grpc;
using Microsoft.Extensions.Logging;

namespace NetFxConsoleApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.UseUtcTimestamp = true;
                    options.TimestampFormat = "yyyy-mm-ddThh:mm:ss.ffffffZ ";
                });
            });

            Channel channel = new("127.0.0.1:4001", ChannelCredentials.Insecure);

            DurableTaskGrpcWorker worker = DurableTaskGrpcWorker.CreateBuilder()
                .AddTasks(tasks =>
                {
                    tasks.AddOrchestrator("HelloSequence", async context =>
                    {
                        var greetings = new List<string>
                        {
                            await context.CallActivityAsync<string>("SayHello", "Tokyo"),
                            await context.CallActivityAsync<string>("SayHello", "London"),
                            await context.CallActivityAsync<string>("SayHello", "Seattle"),
                        };

                        return greetings;
                    });
                    tasks.AddActivity<string, string>("SayHello", (context, city) => $"Hello {city}!");
                })
                .UseLoggerFactory(loggerFactory)
                .UseGrpcChannel(channel)
                .Build();

            await worker.StartAsync(timeout: TimeSpan.FromSeconds(30));

            await using DurableTaskClient client = DurableTaskGrpcClient.CreateBuilder()
                .UseGrpcChannel(channel)
                .Build();

            string instanceId = await client.ScheduleNewOrchestrationInstanceAsync("HelloSequence");
            Console.WriteLine($"Created instance: '{instanceId}'");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1000));
            OrchestrationMetadata instance = await client.WaitForInstanceCompletionAsync(
                instanceId,
                cts.Token,
                getInputsAndOutputs: true);

            Console.WriteLine($"Instance completed: {instance}");
            await channel.ShutdownAsync();
        }
    }
}
