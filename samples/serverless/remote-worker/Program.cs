// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Samples.Serverless.RemoteWorker;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

string endpoint = GetRequiredEnvironmentVariable("DTS_ENDPOINT");
string taskHub = Environment.GetEnvironmentVariable("DTS_TASK_HUB")
    ?? Environment.GetEnvironmentVariable("DTS_TASKHUB")
    ?? "ServerlessPocHub";

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
});

builder.Services.AddDurableTaskWorker(workerBuilder =>
{
    workerBuilder.AddTasks(tasks =>
    {
        tasks.AddActivity<RemoteHelloActivity>();
        tasks.AddActivity<BurstWorkActivity>();
        tasks.AddActivity<ResizeImageActivity>();
        tasks.AddActivity<BurstMegaWorkActivity>();
    });
    workerBuilder.UseDurableTaskScheduler(options =>
    {
        options.EndpointAddress = endpoint;
        options.TaskHubName = taskHub;
    });
    workerBuilder.UseServerlessWorker();
});

await builder.Build().RunAsync();

static string GetRequiredEnvironmentVariable(string name)
    => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"An environment variable named '{name}' is required.");
