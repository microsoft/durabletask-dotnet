// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Samples.OnDemandSandbox.Shared;

namespace Microsoft.DurableTask.Samples.OnDemandSandbox.MainApp;

[SandboxWorkerProfile("remote-hello-profile")]
internal sealed class RemoteHelloSandboxWorkerProfile : ISandboxWorkerProfile
{
    public void Configure(SandboxWorkerProfileOptions options)
    {
        options.ContainerImage = Environment.GetEnvironmentVariable("DTS_SANDBOX_CONTAINER_IMAGE") ?? "on-demand-sandbox-remote-worker:local";
        options.ImagePullManagedIdentityClientId = GetRequiredEnvironmentVariable("DTS_SANDBOX_IMAGE_PULL_UMI_CLIENT_ID");
        options.SchedulerManagedIdentityClientId = GetRequiredEnvironmentVariable("DTS_SANDBOX_SCHEDULER_UMI_CLIENT_ID");
        options.Cpu = "1000m";
        options.Memory = "2048Mi";
        options.MaxConcurrentActivities = 1;
        AddEnvironmentVariable(options, "SAMPLE_REMOTE_MARKER");
        AddEnvironmentVariable(options, "SAMPLE_REMOTE_DELAY_MS");
        options.AddActivity(SandboxActivities.RemoteHelloName, SandboxActivities.RemoteHelloVersion);
    }

    static void AddEnvironmentVariable(SandboxWorkerProfileOptions options, string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value))
        {
            options.EnvironmentVariables[name] = value;
        }
    }

    static string GetRequiredEnvironmentVariable(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} must be set.");
        }

        return value.Trim();
    }
}
