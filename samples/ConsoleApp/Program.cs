// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Grpc;
using Microsoft.Extensions.Logging;

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
    .Build();

await worker.StartAsync(timeout: TimeSpan.FromSeconds(30));

await using DurableTaskClient client = DurableTaskGrpcClient.Create();
string instanceId = await client.ScheduleNewOrchestrationInstanceAsync("HelloSequence");
Console.WriteLine($"Created instance: '{instanceId}'");

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1000));
OrchestrationMetadata instance = await client.WaitForInstanceCompletionAsync(
    instanceId,
    cts.Token,
    getInputsAndOutputs: true);

Console.WriteLine($"Instance completed: {instance}");