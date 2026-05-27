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
string taskHub = Environment.GetEnvironmentVariable("DTS_TASK_HUB") ?? "ServerlessPocHub";
bool skipDeclaration = string.Equals(
    Environment.GetEnvironmentVariable("DTS_SKIP_SERVERLESS_DECLARATION"),
    "true",
    StringComparison.OrdinalIgnoreCase);
DemoCommand command = ParseCommand(
    args,
    Environment.GetEnvironmentVariable("DTS_SAMPLE_HELLO_INPUT") ?? "serverless-sample");
TokenCredential credential = new DefaultAzureCredential();

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

    if (!skipDeclaration)
    {
        workerBuilder.EnableServerlessActivities();
    }
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
    command.OrchestratorName,
    input: command.Input);
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

static DemoCommand ParseCommand(string[] args, string defaultHelloInput)
{
    if (args.Length == 0)
    {
        return new DemoCommand(ServerlessTaskNames.HelloOrchestrator, defaultHelloInput);
    }

    string verb = args[0].ToLowerInvariant();
    return verb switch
    {
        "hello" => new DemoCommand(
            ServerlessTaskNames.HelloOrchestrator,
            args.Length > 1 ? args[1] : defaultHelloInput),
        "long" => new DemoCommand(
            ServerlessTaskNames.LongRunningOrchestrator,
            $"{ParseLongRunningSeconds(args)}|{(args.Length > 2 ? args[2] : defaultHelloInput)}"),
        "mixed" => new DemoCommand(ServerlessTaskNames.MixedLocalRemoteOrchestrator, args.Length > 1 ? args[1] : defaultHelloInput),
        "multi" => new DemoCommand(ServerlessTaskNames.MultiActivityOrchestrator, args.Length > 1 ? args[1] : defaultHelloInput),
        "undeclared" => new DemoCommand(ServerlessTaskNames.UndeclaredActivityOrchestrator, args.Length > 1 ? args[1] : defaultHelloInput),
        "fanout" => new DemoCommand(ServerlessTaskNames.FanOutOrchestrator, $"{GetArgOrDefault(args, 1, "10")}|{GetArgOrDefault(args, 2, "0")}"),
        "env" => new DemoCommand(ServerlessTaskNames.EnvOrchestrator, args.Length > 1 ? args[1] : "SERVERLESS_SAMPLE_MARKER"),
        "fail" => new DemoCommand(ServerlessTaskNames.ExceptionOrchestrator, args.Length > 1 ? args[1] : defaultHelloInput),
        "retry" => new DemoCommand(ServerlessTaskNames.RetryOrchestrator, args.Length > 1 ? args[1] : defaultHelloInput),
        "timer" => new DemoCommand(ServerlessTaskNames.TimerOrchestrator, args.Length > 1 ? args[1] : "5"),
        "crash" => new DemoCommand(ServerlessTaskNames.CrashOrchestrator, args.Length > 1 ? args[1] : defaultHelloInput),
        _ => throw new InvalidOperationException("Supported commands: hello [name], long [seconds] [name], mixed [name], multi [name], undeclared [name], fanout [count] [delaySeconds], env [name], fail [name], retry [name], timer [seconds], crash [name]."),
    };
}

static string GetArgOrDefault(string[] args, int index, string defaultValue) =>
    args.Length > index ? args[index] : defaultValue;

static int ParseLongRunningSeconds(string[] args)
{
    if (args.Length <= 1)
    {
        return 900;
    }

    return int.TryParse(args[1], out int seconds) && seconds > 0
        ? seconds
        : throw new InvalidOperationException("The long command duration must be a positive integer number of seconds.");
}

sealed record DemoCommand(string OrchestratorName, string Input);
