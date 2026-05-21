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
string workerProfileId = Environment.GetEnvironmentVariable("DTS_WORKER_PROFILE_ID") ?? "default";
string[] serverlessActivities = SplitEnvironmentList(Environment.GetEnvironmentVariable("DTS_SERVERLESS_ACTIVITIES"));
int maxConcurrentActivities = GetIntEnv("DTS_SERVERLESS_MAX_ACTIVITIES", 100);
string? substrate = Environment.GetEnvironmentVariable("DTS_SUBSTRATE");
string? dtsSandboxIdentifier = Environment.GetEnvironmentVariable("DTS_SANDBOX_ID");

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
    });
    workerBuilder.UseDurableTaskScheduler(options =>
    {
        options.EndpointAddress = endpoint;
        options.TaskHubName = taskHub;
    });
    workerBuilder.UseServerlessWorker(options =>
    {
        options.TaskHub = taskHub;
        options.WorkerProfileId = workerProfileId;
        options.MaxConcurrentActivities = maxConcurrentActivities;
        options.Substrate = substrate;
        options.DtsSandboxIdentifier = dtsSandboxIdentifier;
        foreach (string activityName in serverlessActivities)
        {
            options.ActivityNames.Add(activityName);
        }
    });
});

await builder.Build().RunAsync();

static string GetRequiredEnvironmentVariable(string name)
    => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"An environment variable named '{name}' is required.");

static string[] SplitEnvironmentList(string? value)
    => string.IsNullOrWhiteSpace(value)
        ? []
        : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

static int GetIntEnv(string name, int defaultValue)
{
    string? value = Environment.GetEnvironmentVariable(name);
    if (string.IsNullOrWhiteSpace(value))
    {
        return defaultValue;
    }

    return int.TryParse(value, out int parsed) && parsed > 0
        ? parsed
        : throw new InvalidOperationException($"Environment variable '{name}' must be a positive integer.");
}
