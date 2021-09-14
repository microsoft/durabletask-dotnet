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

// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

TaskHubGrpcServer server = TaskHubGrpcServer.CreateBuilder()
    .AddTaskOrchestrator("HelloSequence", async context =>
    {
        var greetings = new List<string>
        {
            await context.CallActivityAsync<string>("SayHello", "Tokyo"),
            await context.CallActivityAsync<string>("SayHello", "London"),
            await context.CallActivityAsync<string>("SayHello", "Seattle"),
        };

        return greetings;
    })
    .AddTaskActivity("SayHello", context => $"Hello {context.GetInput<string>()}!")
    .Build();

await server.StartAsync();

await using TaskHubClient client = TaskHubGrpcClient.Create();
string instanceId = await client.ScheduleNewOrchestrationInstanceAsync("HelloSequence");
Console.WriteLine($"Created instance: '{instanceId}'");

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1000));
OrchestrationMetadata instance = await client.WaitForInstanceCompletionAsync(
    instanceId,
    cts.Token,
    getInputsAndOutputs: true);

Console.WriteLine($"Instance completed: {instance}");