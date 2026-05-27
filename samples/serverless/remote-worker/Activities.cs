// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using System.Collections.Concurrent;

namespace Microsoft.DurableTask.Samples.Serverless.RemoteWorker;

static class ActivityAttempts
{
    public static ConcurrentDictionary<string, int> Attempts { get; } = new(StringComparer.Ordinal);
}

[DurableTask("RemoteHello")]
internal sealed class RemoteHelloActivity : TaskActivity<string, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, string input)
        => Task.FromResult($"hello from {Environment.MachineName} pid={Environment.ProcessId}: {input}");
}

[DurableTask("RemoteDelay")]
internal sealed class RemoteDelayActivity : TaskActivity<string, string>
{
    public override async Task<string> RunAsync(TaskActivityContext context, string input)
    {
        string[] parts = input.Split('|', 2);
        int seconds = int.TryParse(parts[0], out int parsedSeconds) && parsedSeconds > 0
            ? parsedSeconds
            : 300;
        string label = parts.Length > 1 ? parts[1] : "long-running-serverless";

        await Task.Delay(TimeSpan.FromSeconds(seconds), CancellationToken.None);
        return $"delayed {seconds}s from {Environment.MachineName} pid={Environment.ProcessId}: {label}";
    }
}

[DurableTask("RemoteEnv")]
internal sealed class RemoteEnvActivity : TaskActivity<string, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, string input)
    {
        string value = Environment.GetEnvironmentVariable(input) ?? "<missing>";
        return Task.FromResult($"{input}={value}");
    }
}

[DurableTask("RemoteIndex")]
internal sealed class RemoteIndexActivity : TaskActivity<string, string>
{
    public override async Task<string> RunAsync(TaskActivityContext context, string input)
    {
        string[] parts = input.Split('|', 2);
        int delaySeconds = parts.Length > 1 && int.TryParse(parts[1], out int parsedDelay) && parsedDelay > 0
            ? parsedDelay
            : 0;
        if (delaySeconds > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), CancellationToken.None);
        }

        return $"{parts[0]}:{Environment.MachineName}:{Environment.ProcessId}";
    }
}

[DurableTask("RemoteFail")]
internal sealed class RemoteFailActivity : TaskActivity<string, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, string input) =>
        throw new InvalidOperationException($"RemoteFail requested: {input}");
}

[DurableTask("RemoteFlaky")]
internal sealed class RemoteFlakyActivity : TaskActivity<string, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, string input)
    {
        int attempt = ActivityAttempts.Attempts.AddOrUpdate(input, 1, static (_, current) => current + 1);
        if (attempt == 1)
        {
            throw new InvalidOperationException($"RemoteFlaky first attempt failed: {input}");
        }

        return Task.FromResult($"flaky succeeded attempt={attempt}: {input}");
    }
}

[DurableTask("RemoteCrash")]
internal sealed class RemoteCrashActivity : TaskActivity<string, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, string input)
    {
        Environment.Exit(42);
        return Task.FromResult($"unreachable: {input}");
    }
}
