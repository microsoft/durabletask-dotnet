// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Samples.OnDemandSandbox.Shared;

namespace Microsoft.DurableTask.Samples.OnDemandSandbox.MainApp;

[DurableTask(nameof(HelloOrchestrator))]
internal sealed class HelloOrchestrator : TaskOrchestrator<string, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        string localResult = await context.CallActivityAsync<string>(OnDemandSandboxTaskNames.LocalHello, input);
        string remoteResult = await context.CallActivityAsync<string>(
            SandboxActivities.RemoteHelloName,
            input,
            new TaskOptions { Version = new TaskVersion(SandboxActivities.RemoteHelloVersion) });
        return $"{localResult}; {remoteResult}";
    }
}
