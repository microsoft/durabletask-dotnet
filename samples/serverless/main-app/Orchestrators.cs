// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;

namespace Microsoft.DurableTask.Samples.Serverless.MainApp;

[DurableTask(nameof(HelloOrchestrator))]
internal sealed class HelloOrchestrator : TaskOrchestrator<string, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        string localResult = await context.CallActivityAsync<string>(ServerlessTaskNames.LocalHello, input);
        string remoteResult = await context.CallActivityAsync<string>(ServerlessTaskNames.RemoteHello, input);
        return $"{localResult}; {remoteResult}";
    }
}
