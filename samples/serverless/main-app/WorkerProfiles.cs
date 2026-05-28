// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Worker.AzureManaged.Serverless;
using Microsoft.DurableTask.Samples.Serverless.Shared;

namespace Microsoft.DurableTask.Samples.Serverless.MainApp;

[ServerlessWorkerProfile("default")]
internal sealed class DefaultServerlessWorkerProfile : IServerlessWorkerProfile
{
    public void Configure(ServerlessOptions options)
    {
        options.ContainerImage = "serverless-remote-worker:local";
        options.Cpu = "1000m";
        options.Memory = "2048Mi";
        options.MaxConcurrentActivities = 1;
        options.AddActivity(ActivityNames.RemoteHello);
    }
}
