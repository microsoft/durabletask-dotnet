// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;

namespace Microsoft.DurableTask.Samples.Serverless.MainApp;

[DurableTask(nameof(HelloOrchestrator))]
internal sealed class HelloOrchestrator : TaskOrchestrator<string, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        string remoteResult = await context.CallActivityAsync<string>(ServerlessTaskNames.RemoteHello, input);
        return remoteResult;
    }
}

[DurableTask(nameof(LongRunningOrchestrator))]
internal sealed class LongRunningOrchestrator : TaskOrchestrator<string, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        string remoteResult = await context.CallActivityAsync<string>(ServerlessTaskNames.RemoteDelay, input);
        return remoteResult;
    }
}

[DurableTask(nameof(MixedLocalRemoteOrchestrator))]
internal sealed class MixedLocalRemoteOrchestrator : TaskOrchestrator<string, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        string before = await context.CallActivityAsync<string>(ServerlessTaskNames.LocalEcho, $"before:{input}");
        string remote = await context.CallActivityAsync<string>(ServerlessTaskNames.RemoteHello, input);
        string after = await context.CallActivityAsync<string>(ServerlessTaskNames.LocalEcho, $"after:{input}");
        return $"{before}|{remote}|{after}";
    }
}

[DurableTask(nameof(MultiActivityOrchestrator))]
internal sealed class MultiActivityOrchestrator : TaskOrchestrator<string, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        string hello = await context.CallActivityAsync<string>(ServerlessTaskNames.RemoteHello, input);
        string env = await context.CallActivityAsync<string>(ServerlessTaskNames.RemoteEnv, "SERVERLESS_SAMPLE_MARKER");
        string index = await context.CallActivityAsync<string>(ServerlessTaskNames.RemoteIndex, "7");
        return $"{hello}|{env}|{index}";
    }
}

[DurableTask(nameof(UndeclaredActivityOrchestrator))]
internal sealed class UndeclaredActivityOrchestrator : TaskOrchestrator<string, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, string input) =>
        context.CallActivityAsync<string>("RemoteNotDeclared", input);
}

[DurableTask(nameof(FanOutOrchestrator))]
internal sealed class FanOutOrchestrator : TaskOrchestrator<string, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        string[] parts = input.Split('|', 2);
        int count = int.TryParse(parts[0], out int parsedCount) && parsedCount > 0 ? parsedCount : 10;
        string delay = parts.Length > 1 ? parts[1] : "0";

        List<Task<string>> tasks = [];
        for (int i = 0; i < count; i++)
        {
            tasks.Add(context.CallActivityAsync<string>(ServerlessTaskNames.RemoteIndex, $"{i}|{delay}"));
        }

        string[] results = await Task.WhenAll(tasks);
        return string.Join(",", results.OrderBy(static item => int.Parse(item.Split(':')[0])));
    }
}

[DurableTask(nameof(EnvOrchestrator))]
internal sealed class EnvOrchestrator : TaskOrchestrator<string, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, string input) =>
        context.CallActivityAsync<string>(ServerlessTaskNames.RemoteEnv, input);
}

[DurableTask(nameof(ExceptionOrchestrator))]
internal sealed class ExceptionOrchestrator : TaskOrchestrator<string, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, string input) =>
        context.CallActivityAsync<string>(ServerlessTaskNames.RemoteFail, input);
}

[DurableTask(nameof(RetryOrchestrator))]
internal sealed class RetryOrchestrator : TaskOrchestrator<string, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        TaskOptions options = new(new RetryPolicy(3, TimeSpan.FromSeconds(1)));
        return context.CallActivityAsync<string>(ServerlessTaskNames.RemoteFlaky, input, options);
    }
}

[DurableTask(nameof(TimerOrchestrator))]
internal sealed class TimerOrchestrator : TaskOrchestrator<string, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        int seconds = int.TryParse(input, out int parsedSeconds) && parsedSeconds > 0 ? parsedSeconds : 5;
        await context.CreateTimer(TimeSpan.FromSeconds(seconds), CancellationToken.None);
        return await context.CallActivityAsync<string>(ServerlessTaskNames.RemoteHello, $"timer:{seconds}");
    }
}

[DurableTask(nameof(CrashOrchestrator))]
internal sealed class CrashOrchestrator : TaskOrchestrator<string, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, string input) =>
        context.CallActivityAsync<string>(ServerlessTaskNames.RemoteCrash, input);
}
