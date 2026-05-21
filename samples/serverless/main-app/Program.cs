// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
using Azure.Identity;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Samples.Serverless.MainApp;
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
string serverlessActivityImage = Environment.GetEnvironmentVariable("DTS_SERVERLESS_ACTIVITY_IMAGE")
    ?? "serverless-remote-worker:local";
string helloInput = Environment.GetEnvironmentVariable("DTS_SAMPLE_HELLO_INPUT") ?? "serverless-sample";
TokenCredential credential = new DefaultAzureCredential();
DemoCommand command = ParseCommand(args, helloInput);

if (command.Kind == DemoCommandKind.Serve)
{
    await ServerlessSandboxHttpHost.RunAsync(
        endpoint,
        taskHub,
        workerProfileId,
        credential);
    return;
}

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
});

builder.Services.AddDurableTaskWorker(workerBuilder =>
{
    workerBuilder.AddTasks(tasks => tasks.AddAllGeneratedTasks());
    workerBuilder.UseDurableTaskScheduler(options =>
    {
        options.EndpointAddress = endpoint;
        options.TaskHubName = taskHub;
        options.Credential = credential;
    });

    workerBuilder.DeclareServerlessActivities(options =>
    {
        options.TaskHub = taskHub;
        options.WorkerProfileId = workerProfileId;
        options.ContainerImage = serverlessActivityImage;
        options.Cpu = Environment.GetEnvironmentVariable("DTS_SERVERLESS_CPU") ?? "1000m";
        options.Memory = Environment.GetEnvironmentVariable("DTS_SERVERLESS_MEMORY") ?? "2048Mi";
        options.MaxConcurrentActivities = GetIntEnv("DTS_SERVERLESS_MAX_ACTIVITIES", 1);
        options.EnvironmentVariables["DTS_ENDPOINT"] = endpoint;
        options.ActivityNames.Add(ServerlessTaskNames.RemoteHello);
    });
});

builder.Services.AddDurableTaskClient(clientBuilder =>
{
    clientBuilder.UseDurableTaskScheduler(options =>
    {
        options.EndpointAddress = endpoint;
        options.TaskHubName = taskHub;
        options.Credential = credential;
    });
});

using IHost host = builder.Build();

await host.StartAsync();

DurableTaskClient client = host.Services.GetRequiredService<DurableTaskClient>();
string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
    ServerlessTaskNames.HelloOrchestrator,
    input: command.HelloInput);
OrchestrationMetadata? result = await client.WaitForInstanceCompletionAsync(
    instanceId,
    getInputsAndOutputs: true);

Console.WriteLine($"Started orchestration: {instanceId}");
Console.WriteLine($"Runtime status: {result?.RuntimeStatus}");
Console.WriteLine($"Output: {result?.SerializedOutput ?? "<null>"}");

await host.StopAsync();

static string GetRequiredEnvironmentVariable(string name)
    => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"An environment variable named '{name}' is required.");

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

static DemoCommand ParseCommand(string[] args, string defaultHelloInput)
{
    if (args.Length == 0)
    {
        return DemoCommand.Hello(defaultHelloInput);
    }

    string verb = args[0].ToLowerInvariant();
    return verb switch
    {
        "hello" => DemoCommand.Hello(args.Length > 1 ? args[1] : defaultHelloInput),
        "serve" or "http" or "api" => DemoCommand.Serve,
        _ => throw new InvalidOperationException("Supported commands: hello [name], serve."),
    };
}

internal enum DemoCommandKind
{
    Hello,
    Serve,
}

internal sealed record DemoCommand(DemoCommandKind Kind, string HelloInput)
{
    public static DemoCommand Serve { get; } = new(DemoCommandKind.Serve, string.Empty);

    public static DemoCommand Hello(string input) => new(DemoCommandKind.Hello, input);
}
