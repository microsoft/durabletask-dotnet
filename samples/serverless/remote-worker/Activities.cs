// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;

namespace Microsoft.DurableTask.Samples.Serverless.RemoteWorker;

[DurableTask("RemoteHello")]
internal sealed class RemoteHelloActivity : TaskActivity<string, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, string input)
    {
        string marker = Environment.GetEnvironmentVariable("SERVERLESS_SAMPLE_MARKER") ?? "<missing>";
        return Task.FromResult($"hello remotely from {Environment.MachineName} pid={Environment.ProcessId}: {input}; marker={marker}");
    }
}
