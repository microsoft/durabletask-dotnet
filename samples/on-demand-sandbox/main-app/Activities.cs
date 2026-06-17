// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;

namespace Microsoft.DurableTask.Samples.OnDemandSandbox.MainApp;

internal static class OnDemandSandboxTaskNames
{
    public const string LocalHello = "LocalHello";
    public const string HelloOrchestrator = nameof(HelloOrchestrator);
}

[DurableTask(OnDemandSandboxTaskNames.LocalHello)]
internal sealed class LocalHelloActivity : TaskActivity<string, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, string input)
        => Task.FromResult($"hello locally: {input}");
}

