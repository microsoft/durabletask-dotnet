// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Samples.Serverless.Shared;

namespace Microsoft.DurableTask.Samples.Serverless.RemoteWorker;

[DurableTask(ActivityNames.RemoteHello)]
internal sealed class RemoteHelloActivity : TaskActivity<string, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, string input)
    {
        return Task.FromResult($"hello remotely from {Environment.MachineName} pid={Environment.ProcessId}: {input}");
    }
}
