// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;
using Proto = Microsoft.DurableTask.Protobuf.OnDemandSandbox;

namespace Microsoft.DurableTask.Worker.AzureManaged.OnDemandSandbox;

/// <summary>
/// Builds and normalizes on-demand sandbox activity protocol messages.
/// </summary>
static class OnDemandSandboxActivityConfiguration
{
    /// <summary>
    /// Resolves configured activity names for on-demand sandbox activity execution.
    /// </summary>
    /// <param name="configuredNames">The configured activity names.</param>
    /// <returns>The normalized activity names.</returns>
    public static string[] ResolveActivityNames(IEnumerable<string> configuredNames)
    {
        return configuredNames
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Builds an on-demand sandbox activity declaration protocol message.
    /// </summary>
    /// <param name="options">The on-demand sandbox options.</param>
    /// <param name="activityNames">The activity names included in the declaration.</param>
    /// <returns>The declaration protocol message.</returns>
    public static Proto.OnDemandSandboxActivityDeclaration BuildDeclaration(OnDemandSandboxOptions options, IReadOnlyCollection<string> activityNames)
    {
        Check.NotNull(options);
        Check.NotNull(activityNames);

        ValidateTaskHub(options.TaskHub, "On-demand sandbox activity declaration requires a task hub name.");

        if (activityNames.Count == 0)
        {
            throw new InvalidOperationException("On-demand sandbox activity declaration requires at least one activity name.");
        }

        string workerProfileId = NormalizeWorkerProfileId(options.WorkerProfileId, "On-demand sandbox activity declaration requires a worker profile ID.");

        if (options.MaxConcurrentActivities <= 0)
        {
            throw new InvalidOperationException("On-demand sandbox activity max concurrent activities must be greater than zero.");
        }

        string schedulerManagedIdentityClientId = NormalizeRequired(
            options.SchedulerManagedIdentityClientId ?? string.Empty,
            "On-demand sandbox activity declaration requires the managed identity client ID workers use to connect to the DTS scheduler.");

        Proto.OnDemandSandboxActivityDeclaration declaration = new()
        {
            WorkerProfileId = workerProfileId,
            Image = BuildImage(options),
            Resources = BuildResources(options),
            MaxConcurrentActivities = options.MaxConcurrentActivities,
            SchedulerManagedIdentityClientId = schedulerManagedIdentityClientId,
        };

        declaration.ActivityNames.AddRange(activityNames);
        declaration.EnvironmentVariables.Add(options.EnvironmentVariables);
        declaration.Entrypoint.AddRange(NormalizeOptionalStrings(options.Entrypoint));
        declaration.Cmd.AddRange(NormalizeOptionalStrings(options.Cmd));
        return declaration;
    }

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

        ValidateTaskHub(options.TaskHub, "On-demand sandbox activity worker registration requires a task hub name.");
        string[] activityNames = ResolveActivityNames(registeredActivityNames);
        if (activityNames.Length == 0)
        {
            throw new InvalidOperationException("On-demand sandbox activity worker registration requires at least one registered activity.");
        }

        if (options.MaxConcurrentActivities <= 0)
        {
            throw new InvalidOperationException("On-demand sandbox activity worker max concurrent activities must be greater than zero.");
        }

        string workerProfileId = NormalizeWorkerProfileId(options.WorkerProfileId, "On-demand sandbox activity worker registration requires a worker profile ID.");

        Proto.OnDemandSandboxActivityWorkerStart start = new()
        {
            TaskHub = options.TaskHub,
            WorkerProfileId = workerProfileId,
            MaxActivitiesCount = options.MaxConcurrentActivities,
            Substrate = GetSubstrateFromEnvironment(),
            DtsSandboxIdentifier = Environment.GetEnvironmentVariable("DTS_SANDBOX_ID") ?? string.Empty,
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

    static Proto.OnDemandSandboxActivityImage BuildImage(OnDemandSandboxOptions options)
    {
        string imageRef = NormalizeRequired(
            options.ContainerImage ?? string.Empty,
            "On-demand sandbox activity image metadata requires a container image reference like 'myregistry.azurecr.io/workers/hello:1.0' or 'myregistry.azurecr.io/workers/hello@sha256:...'.");

        Proto.OnDemandSandboxActivityImage image = new()
        {
            ImageRef = imageRef,
            ManagedIdentityClientId = NormalizeRequired(
                options.ImagePullManagedIdentityClientId ?? string.Empty,
                "On-demand sandbox activity declaration requires the managed identity client ID ADC uses to pull the worker image."),
        };

        return image;
    }

    static Proto.OnDemandSandboxActivityResources BuildResources(OnDemandSandboxOptions options)
    {
        string cpu = NormalizeCpu(options.Cpu);
        string memory = NormalizeMemory(options.Memory);

        return new Proto.OnDemandSandboxActivityResources
        {
            Cpu = cpu,
            Memory = memory,
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

    static void ValidateTaskHub(string value, string errorMessage)
    {
        _ = NormalizeRequired(value, errorMessage);
    }

    static string NormalizeWorkerProfileId(string value, string errorMessage)
    {
        return NormalizeRequired(value, errorMessage);
    }

    static string NormalizeRequired(string value, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(errorMessage);
        }

        return value.Trim();
    }

    static string NormalizeCpu(string value)
    {
        string normalized = NormalizeRequired(value, "On-demand sandbox activity declaration requires CPU resources.");
        if (TryParseCpuMillicores(normalized) is not { } milliCpu || milliCpu <= 0)
        {
            throw new InvalidOperationException(
                "On-demand sandbox activity CPU resources must be a positive Kubernetes-style CPU quantity. " +
                "Use formats like '500m', '2', or '0.5'.");
        }

        return normalized;
    }

    static string NormalizeMemory(string value)
    {
        string normalized = NormalizeRequired(value, "On-demand sandbox activity declaration requires memory resources.");
        if (TryParseMemoryMiB(normalized) is not { } memoryMiB || memoryMiB <= 0)
        {
            throw new InvalidOperationException(
                "On-demand sandbox activity memory resources must be a positive Kubernetes-style memory quantity. " +
                "Use formats like '256Mi', '1Gi', or '2048'.");
        }

        return normalized;
    }

    static long? TryParseCpuMillicores(string value)
    {
        if (value.EndsWith('m') || value.EndsWith('M'))
        {
            return decimal.TryParse(
                value[..^1],
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal milliCpu)
                ? (long)milliCpu
                : null;
        }

        return decimal.TryParse(
            value,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out decimal cores)
            ? (long)(cores * 1000)
            : null;
    }

    static long? TryParseMemoryMiB(string value)
    {
        if (value.EndsWith("Gi", StringComparison.OrdinalIgnoreCase))
        {
            return decimal.TryParse(
                value[..^2],
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal gib)
                ? (long)(gib * 1024)
                : null;
        }

        if (value.EndsWith("Mi", StringComparison.OrdinalIgnoreCase))
        {
            return decimal.TryParse(
                value[..^2],
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal mib)
                ? (long)mib
                : null;
        }

        return decimal.TryParse(
            value,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out decimal parsed)
            ? (long)parsed
            : null;
    }

    static string[] NormalizeOptionalStrings(IEnumerable<string> values)
    {
        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToArray();
    }
}
