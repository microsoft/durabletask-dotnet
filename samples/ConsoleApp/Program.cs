//  ----------------------------------------------------------------------------------
//  Copyright Microsoft Corporation
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//  http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//  ----------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using DurableTask;
using DurableTask.Grpc;
using Microsoft.Extensions.Logging;

// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

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

TaskHubGrpcWorker server = TaskHubGrpcWorker.CreateBuilder()
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
        tasks.AddActivity("SayHello", context => $"Hello {context.GetInput<string>()}!");
    })
    .UseLoggerFactory(loggerFactory)
    .Build();

await server.StartAsync(timeout: TimeSpan.FromSeconds(30));

await using TaskHubClient client = TaskHubGrpcClient.Create();
string instanceId = await client.ScheduleNewOrchestrationInstanceAsync("HelloSequence");
Console.WriteLine($"Created instance: '{instanceId}'");

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1000));
OrchestrationMetadata instance = await client.WaitForInstanceCompletionAsync(
    instanceId,
    cts.Token,
    getInputsAndOutputs: true);

Console.WriteLine($"Instance completed: {instance}");