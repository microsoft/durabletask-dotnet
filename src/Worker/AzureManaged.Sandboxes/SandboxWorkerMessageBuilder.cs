// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.AzureManaged.Internal;
using Proto = Microsoft.DurableTask.Protobuf.Sandboxes;

namespace Microsoft.DurableTask.Worker.AzureManaged.Sandboxes;

/// <summary>
/// Builds on-demand sandbox activity worker registration protocol messages.
/// </summary>
static class SandboxWorkerMessageBuilder
{
    /// <summary>
    /// Builds the initial on-demand sandbox activity worker registration message.
    /// </summary>
    /// <param name="options">The on-demand sandbox options.</param>
    /// <param name="registeredActivities">The activity handlers registered by the worker process.</param>
    /// <returns>The worker start protocol message.</returns>
    public static Proto.SandboxActivityWorkerMessage BuildWorkerStart(
        SandboxWorkerRuntimeOptions options,
        IReadOnlyCollection<SandboxActivityMetadata.Activity> registeredActivities)
    {
        Check.NotNull(options);
        Check.NotNull(registeredActivities);

        string taskHub = SandboxActivityMetadata.NormalizeRequired(
            options.TaskHub,
            "On-demand sandbox activity worker registration requires a task hub name.");
        SandboxActivityMetadata.Activity[] activities = SandboxActivityMetadata.ResolveActivities(registeredActivities);
        if (activities.Length == 0)
        {
            throw new InvalidOperationException("On-demand sandbox activity worker registration requires at least one registered activity.");
        }

        if (options.MaxConcurrentActivities <= 0)
        {
            throw new InvalidOperationException("On-demand sandbox activity worker max activity count must be greater than zero.");
        }

        string workerProfileId = SandboxActivityMetadata.NormalizeRequired(
            options.WorkerProfileId,
            "On-demand sandbox activity worker registration requires a worker profile ID.");
        string dtsSandboxIdentifier = SandboxActivityMetadata.NormalizeRequired(
            Environment.GetEnvironmentVariable(SandboxWorkerEnvironmentVariables.SandboxId) ?? string.Empty,
            "On-demand sandbox activity worker registration requires a DTS sandbox ID.");

        Proto.SandboxActivityWorkerStart start = new()
        {
            TaskHub = taskHub,
            WorkerProfileId = workerProfileId,
            MaxActivitiesCount = options.MaxConcurrentActivities,
            SandboxProvider = GetSandboxProviderFromEnvironment(),
            DtsSandboxIdentifier = dtsSandboxIdentifier,
        };
        start.Activities.AddRange(activities.Select(ToProtoActivity));

        return new Proto.SandboxActivityWorkerMessage { Start = start };
    }

    /// <summary>
    /// Builds an on-demand sandbox activity worker heartbeat message.
    /// </summary>
    /// <param name="activeActivitiesCount">The number of activities currently executing.</param>
    /// <returns>The heartbeat protocol message.</returns>
    public static Proto.SandboxActivityWorkerMessage BuildWorkerHeartbeat(int activeActivitiesCount)
    {
        if (activeActivitiesCount < 0)
        {
            throw new InvalidOperationException("On-demand sandbox activity worker active activity count cannot be negative.");
        }

        return new Proto.SandboxActivityWorkerMessage
        {
            Heartbeat = new Proto.SandboxActivityWorkerHeartbeat
            {
                ActiveActivitiesCount = activeActivitiesCount,
            },
        };
    }

    static Proto.SandboxActivity ToProtoActivity(SandboxActivityMetadata.Activity activity) => new()
    {
        Name = activity.Name,
        Version = activity.Version ?? string.Empty,
    };

    static Proto.SandboxProviderKind GetSandboxProviderFromEnvironment()
    {
        string? sandboxProvider = Environment.GetEnvironmentVariable(SandboxWorkerEnvironmentVariables.SandboxProvider);
        if (sandboxProvider is null)
        {
            return Proto.SandboxProviderKind.Unspecified;
        }

        if (sandboxProvider.Equals("Sandbox", StringComparison.OrdinalIgnoreCase))
        {
            return Proto.SandboxProviderKind.Sandbox;
        }

        if (sandboxProvider.Equals("AcaSessionPool", StringComparison.OrdinalIgnoreCase))
        {
            return Proto.SandboxProviderKind.AcaSessionPool;
        }

        return Proto.SandboxProviderKind.Unspecified;
    }
}
