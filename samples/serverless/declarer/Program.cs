// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
using Azure.Identity;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Samples.Serverless.Declarer;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

DemoCommandParseResult parseResult = TryParseCommand(args, out DemoCommand command);
if (parseResult == DemoCommandParseResult.Invalid)
{
    return;
}

string endpoint = GetRequiredEnvironmentVariable("DTS_ENDPOINT");
string taskHub = Environment.GetEnvironmentVariable("DTS_TASK_HUB")
    ?? Environment.GetEnvironmentVariable("DTS_TASKHUB")
    ?? "ServerlessPocHub";
bool allowInsecureCredentials = endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
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
        options.AllowInsecureCredentials = allowInsecureCredentials;
    });

    workerBuilder.DeclareServerlessActivities(options =>
    {
        options.TaskHub = taskHub;
        options.WorkerProfileId = Environment.GetEnvironmentVariable("DTS_WORKER_PROFILE_ID") ?? "default";
        options.ContainerImage = Environment.GetEnvironmentVariable("DTS_SERVERLESS_ACTIVITY_IMAGE")
            ?? "serverless-remote-worker:local";
        options.Cpu = Environment.GetEnvironmentVariable("DTS_SERVERLESS_CPU") ?? "1000m";
        options.Memory = Environment.GetEnvironmentVariable("DTS_SERVERLESS_MEMORY") ?? "2048Mi";
        options.MaxConcurrentActivities = GetIntEnv("DTS_SERVERLESS_MAX_ACTIVITIES", 100);
        options.EnvironmentVariables["DTS_ENDPOINT"] = endpoint;
        AddDeclarationEnvironmentVariableIfPresent(options.EnvironmentVariables, "DTS_SERVERLESS_IDLE_TIMEOUT_SECONDS");
        AddServerlessActivityNames(options.ActivityNames);
    });
});

builder.Services.AddDurableTaskClient(clientBuilder =>
{
    clientBuilder.UseDurableTaskScheduler(options =>
    {
        options.EndpointAddress = endpoint;
        options.TaskHubName = taskHub;
        options.Credential = credential;
        options.AllowInsecureCredentials = allowInsecureCredentials;
    });
});

using IHost host = builder.Build();

if (parseResult == DemoCommandParseResult.Execute)
{
    await host.StartAsync();

    DurableTaskClient client = host.Services.GetRequiredService<DurableTaskClient>();
    await ExecuteCommandAsync(client, command);

    await host.StopAsync();
    return;
}

if (parseResult == DemoCommandParseResult.RunHttpApi)
{
    await ServerlessSandboxHttpHost.RunAsync(
        endpoint,
        taskHub,
        credential);
    return;
}

await host.RunAsync();

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

static void AddDeclarationEnvironmentVariableIfPresent(IDictionary<string, string> environmentVariables, string name)
{
    string? value = Environment.GetEnvironmentVariable(name);
    if (!string.IsNullOrWhiteSpace(value))
    {
        environmentVariables[name] = value;
    }
}

static void AddServerlessActivityNames(ICollection<string> activityNames)
{
    activityNames.Add(ServerlessTaskNames.RemoteHello);
    activityNames.Add(ServerlessTaskNames.BurstWork);
    activityNames.Add(ServerlessTaskNames.ResizeImage);
    activityNames.Add(ServerlessTaskNames.BurstMegaWork);
}

static DemoCommandParseResult TryParseCommand(string[] args, out DemoCommand command)
{
    command = DemoCommand.RunWorker;

    if (args.Length == 0)
    {
        return DemoCommandParseResult.RunWorker;
    }

    string verb = args[0].ToLowerInvariant();
    switch (verb)
    {
        case "hello":
            command = DemoCommand.Hello(args.Length > 1 ? args[1] : "world");
            return DemoCommandParseResult.Execute;
        case "burst":
            int burstCount = args.Length > 1 && int.TryParse(args[1], out int parsedCount) ? parsedCount : 10;
            command = DemoCommand.Burst(burstCount);
            return DemoCommandParseResult.Execute;
        case "resize":
            string sourceUri = args.Length > 1 ? args[1] : "https://example.invalid/sample.png";
            int width = args.Length > 2 && int.TryParse(args[2], out int parsedWidth) ? parsedWidth : 160;
            int height = args.Length > 3 && int.TryParse(args[3], out int parsedHeight) ? parsedHeight : 90;
            command = DemoCommand.Resize(new ResizeImageRequest(sourceUri, width, height));
            return DemoCommandParseResult.Execute;
        case "burst-mega":
            int megaCount = args.Length > 1 && int.TryParse(args[1], out int parsedMega) ? parsedMega : 50;
            command = DemoCommand.BurstMega(megaCount);
            return DemoCommandParseResult.Execute;
        case "serve":
        case "http":
        case "api":
            command = DemoCommand.RunHttpApi;
            return DemoCommandParseResult.RunHttpApi;
        default:
            Console.WriteLine("Unknown command. Supported commands: hello [name], burst [count], burst-mega [count], resize [url] [width] [height], serve.");
            Environment.ExitCode = 1;
            return DemoCommandParseResult.Invalid;
    }
}

static async Task ExecuteCommandAsync(DurableTaskClient client, DemoCommand command)
{
    switch (command.Kind)
    {
        case DemoCommandKind.Hello:
            await RunAndPrintAsync(client, ServerlessTaskNames.HelloOrchestrator, command.HelloInput!);
            break;
        case DemoCommandKind.Burst:
            await RunAndPrintAsync(client, ServerlessTaskNames.BurstOrchestrator, command.BurstCount!.Value);
            break;
        case DemoCommandKind.Resize:
            await RunAndPrintAsync(client, ServerlessTaskNames.ResizeImageOrchestrator, command.ResizeRequest!);
            break;
        case DemoCommandKind.BurstMega:
            await RunAndPrintAsync(client, ServerlessTaskNames.BurstMegaOrchestrator, command.BurstCount!.Value);
            break;
    }
}

static async Task RunAndPrintAsync(DurableTaskClient client, string orchestratorName, object input)
{
    string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, input: input);
    OrchestrationMetadata? result = await client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: true);
    Console.WriteLine($"Started orchestration: {instanceId}");
    Console.WriteLine($"Runtime status: {result?.RuntimeStatus}");
    Console.WriteLine($"Output: {result?.SerializedOutput ?? "<null>"}");
}

internal enum DemoCommandParseResult
{
    RunWorker,
    Execute,
    RunHttpApi,
    Invalid,
}

internal enum DemoCommandKind
{
    RunWorker,
    RunHttpApi,
    Hello,
    Burst,
    Resize,
    BurstMega,
}

internal sealed record DemoCommand(DemoCommandKind Kind, string? HelloInput = null, int? BurstCount = null, ResizeImageRequest? ResizeRequest = null)
{
    public static DemoCommand RunWorker { get; } = new(DemoCommandKind.RunWorker);

    public static DemoCommand RunHttpApi { get; } = new(DemoCommandKind.RunHttpApi);

    public static DemoCommand Hello(string input) => new(DemoCommandKind.Hello, HelloInput: input);

    public static DemoCommand Burst(int input) => new(DemoCommandKind.Burst, BurstCount: input);

    public static DemoCommand Resize(ResizeImageRequest request) => new(DemoCommandKind.Resize, ResizeRequest: request);

    public static DemoCommand BurstMega(int count) => new(DemoCommandKind.BurstMega) { BurstCount = count };
}
