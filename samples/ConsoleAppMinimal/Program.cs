// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// This app differs from samples/ConsoleApp in that we show the absolute minimum code needed to run a Durable Task application.

using ConsoleAppMinimal;
using Dapr.DurableTask.Client;
using Dapr.DurableTask.Worker;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDurableTaskClient().UseGrpc();
builder.Services.AddDurableTaskWorker()
    .AddTasks(tasks =>
    {
        tasks.AddOrchestrator<HelloSequenceOrchestrator>();
        tasks.AddActivity<SayHelloActivity>();
    })
    .UseGrpc();

IHost host = builder.Build();
await host.StartAsync();
