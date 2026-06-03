// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Worker.AzureManaged.OnDemandSandbox;
using Microsoft.DurableTask.Samples.OnDemandSandbox.Shared;

namespace Microsoft.DurableTask.Samples.OnDemandSandbox.MainApp;

[OnDemandSandboxWorkerProfile("default")]
internal sealed class DefaultSandboxWorkerProfile : ISandboxWorkerProfile
{
    public void Configure(OnDemandSandboxOptions options)
    {
        options.ContainerImage = Environment.GetEnvironmentVariable("DTS_ON_DEMAND_SANDBOX_CONTAINER_IMAGE") ?? "on-demand-sandbox-remote-worker:local";
        options.ImagePullManagedIdentityClientId = GetRequiredEnvironmentVariable("DTS_ON_DEMAND_SANDBOX_IMAGE_PULL_UMI_CLIENT_ID");
        options.SchedulerManagedIdentityClientId = GetRequiredEnvironmentVariable("DTS_ON_DEMAND_SANDBOX_SCHEDULER_UMI_CLIENT_ID");
        options.Cpu = "1000m";
        options.Memory = "2048Mi";
        options.MaxConcurrentActivities = 1;
        AddEnvironmentVariable(options, "SAMPLE_REMOTE_MARKER");
        AddEnvironmentVariable(options, "SAMPLE_REMOTE_DELAY_MS");
        options.AddActivity(ActivityNames.RemoteHello);
    }

    static void AddEnvironmentVariable(OnDemandSandboxOptions options, string name)
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

        return value;
    }
}
