// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.AzureManaged.Internal;
using Proto = Microsoft.DurableTask.Protobuf.OnDemandSandbox;

namespace Microsoft.DurableTask.Worker.AzureManaged.OnDemandSandbox;

/// <summary>
/// Builds on-demand sandbox activity worker registration protocol messages.
/// </summary>
static class OnDemandSandboxWorkerMessageBuilder
{
    /// <summary>
    /// Builds the initial on-demand sandbox activity worker registration message.
    /// </summary>
    /// <param name="options">The on-demand sandbox options.</param>
    /// <param name="registeredActivityNames">The activity handlers registered by the worker process.</param>
    /// <returns>The worker start protocol message.</returns>
    public static Proto.OnDemandSandboxActivityWorkerMessage BuildWorkerStart(
        OnDemandSandboxWorkerRuntimeOptions options,
        IReadOnlyCollection<string> registeredActivityNames)
    {
        Check.NotNull(options);
        Check.NotNull(registeredActivityNames);

        string taskHub = OnDemandSandboxActivityMetadata.NormalizeRequired(
            options.TaskHub,
            "On-demand sandbox activity worker registration requires a task hub name.");
        string[] activityNames = OnDemandSandboxActivityMetadata.ResolveActivityNames(registeredActivityNames);
        if (activityNames.Length == 0)
        {
            throw new InvalidOperationException("On-demand sandbox activity worker registration requires at least one registered activity.");
        }

        if (options.MaxConcurrentActivities <= 0)
        {
            throw new InvalidOperationException("On-demand sandbox activity worker max activity count must be greater than zero.");
        }

        string workerProfileId = OnDemandSandboxActivityMetadata.NormalizeWorkerProfileId(
            options.WorkerProfileId,
            "On-demand sandbox activity worker registration requires a worker profile ID.");
        string dtsSandboxIdentifier = OnDemandSandboxActivityMetadata.NormalizeRequired(
            Environment.GetEnvironmentVariable("DTS_SANDBOX_ID") ?? string.Empty,
            "On-demand sandbox activity worker registration requires a DTS sandbox ID.");

        Proto.OnDemandSandboxActivityWorkerStart start = new()
        {
            TaskHub = taskHub,
            WorkerProfileId = workerProfileId,
            MaxActivitiesCount = options.MaxConcurrentActivities,
            Substrate = GetSubstrateFromEnvironment(),
            DtsSandboxIdentifier = dtsSandboxIdentifier,
        };
        start.ActivityNames.AddRange(activityNames);

        return new Proto.OnDemandSandboxActivityWorkerMessage { Start = start };
    }

    /// <summary>
    /// Builds an on-demand sandbox activity worker heartbeat message.
    /// </summary>
    /// <param name="activeActivitiesCount">The number of activities currently executing.</param>
    /// <returns>The heartbeat protocol message.</returns>
    public static Proto.OnDemandSandboxActivityWorkerMessage BuildWorkerHeartbeat(int activeActivitiesCount)
    {
        if (activeActivitiesCount < 0)
        {
            throw new InvalidOperationException("On-demand sandbox activity worker active activity count cannot be negative.");
        }

        return new Proto.OnDemandSandboxActivityWorkerMessage
        {
            Heartbeat = new Proto.OnDemandSandboxActivityWorkerHeartbeat
            {
                ActiveActivitiesCount = activeActivitiesCount,
            },
        };
    }

    static Proto.SubstrateKind GetSubstrateFromEnvironment()
    {
        string? substrate = Environment.GetEnvironmentVariable("DTS_SUBSTRATE");
        if (substrate is null)
        {
            return Proto.SubstrateKind.Unspecified;
        }

        if (substrate.Equals("Sandbox", StringComparison.OrdinalIgnoreCase))
        {
            return Proto.SubstrateKind.Sandbox;
        }

        if (substrate.Equals("AcaSessionPool", StringComparison.OrdinalIgnoreCase))
        {
            return Proto.SubstrateKind.AcaSessionPool;
        }

        return Proto.SubstrateKind.Unspecified;
    }
}
