// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Worker.AzureManaged.Serverless;

namespace Microsoft.DurableTask.Samples.Serverless.MainApp;

internal static class ServerlessTaskNames
{
    public const string LocalHello = "LocalHello";
    public const string RemoteHello = "RemoteHello";
    public const string HelloOrchestrator = nameof(HelloOrchestrator);
}

[DurableTask(ServerlessTaskNames.LocalHello)]
internal sealed class LocalHelloActivity : TaskActivity<string, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, string input)
        => Task.FromResult($"hello locally: {input}");
}

[ServerlessActivity("default", Name = ServerlessTaskNames.RemoteHello)]
internal sealed class RemoteHelloDeclaration;
