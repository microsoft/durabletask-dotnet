// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;
using Microsoft.DurableTask.AzureManaged.Internal;
using Proto = Microsoft.DurableTask.Protobuf.Sandboxes;

namespace Microsoft.DurableTask.Client.AzureManaged;

/// <summary>
/// Builds and normalizes on-demand sandbox activity workerProfile protocol messages.
/// </summary>
static class SandboxWorkerProfileBuilder
{
    const long MinCpuMillicores = 250;
    const long MaxCpuMillicores = 16_000;
    const long CpuStepMillicores = 250;
    const long MemoryMiBPerCore = 2 * 1024;

    /// <summary>
    /// Resolves configured activity identities for on-demand sandbox activity execution.
    /// </summary>
    /// <param name="configuredActivities">The configured activity identities.</param>
    /// <returns>The normalized activity identities.</returns>
    public static SandboxActivityMetadata.Activity[] ResolveActivities(IEnumerable<SandboxActivityMetadata.Activity> configuredActivities)
    {
        return SandboxActivityMetadata.ResolveActivities(configuredActivities);
    }

    /// <summary>
    /// Builds an on-demand sandbox activity workerProfile protocol message.
    /// </summary>
    /// <param name="options">The on-demand sandbox options.</param>
    /// <param name="activities">The activity identities included in the workerProfile.</param>
    /// <returns>The workerProfile protocol message.</returns>
    public static Proto.SandboxWorkerProfile BuildWorkerProfile(
        SandboxWorkerProfileOptions options,
        IReadOnlyCollection<SandboxActivityMetadata.Activity> activities)
    {
        Check.NotNull(options);
        Check.NotNull(activities);

        _ = NormalizeRequired(options.TaskHub, "On-demand sandbox activity workerProfile requires a task hub name.");
        if (activities.Count == 0)
        {
            throw new InvalidOperationException("On-demand sandbox activity workerProfile requires at least one activity.");
        }

        string workerProfileId = NormalizeWorkerProfileId(
            options.WorkerProfileId,
            "On-demand sandbox activity workerProfile requires a worker profile ID.");
        if (options.MaxConcurrentActivities <= 0)
        {
            throw new InvalidOperationException("On-demand sandbox activity max concurrent activities must be greater than zero.");
        }

        string schedulerManagedIdentityClientId = NormalizeRequired(
            options.SchedulerManagedIdentityClientId ?? string.Empty,
            "On-demand sandbox activity workerProfile requires the managed identity client ID workers use to connect to the DTS scheduler.");

        Proto.SandboxWorkerProfile workerProfile = new()
        {
            WorkerProfileId = workerProfileId,
            Image = BuildImage(options),
            Resources = BuildResources(options),
            MaxConcurrentActivities = options.MaxConcurrentActivities,
            SchedulerManagedIdentityClientId = schedulerManagedIdentityClientId,
        };

        workerProfile.Activities.AddRange(activities.Select(ToProtoActivity));
        workerProfile.EnvironmentVariables.Add(options.EnvironmentVariables);
        workerProfile.Image.Entrypoint.AddRange(NormalizeOptionalStrings(options.Entrypoint));
        workerProfile.Image.Cmd.AddRange(NormalizeOptionalStrings(options.Cmd));
        return workerProfile;
    }

    /// <summary>
    /// Normalizes a worker profile ID and throws with the supplied message if it is missing.
    /// </summary>
    /// <param name="value">The worker profile ID value.</param>
    /// <param name="errorMessage">The error message to use when the value is missing.</param>
    /// <returns>The normalized worker profile ID.</returns>
    internal static string NormalizeWorkerProfileId(string value, string errorMessage)
    {
        return SandboxActivityMetadata.NormalizeRequired(value, errorMessage);
    }

    /// <summary>
    /// Normalizes a required string and throws with the supplied message if it is missing.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="errorMessage">The error message to use when the value is missing.</param>
    /// <returns>The normalized value.</returns>
    internal static string NormalizeRequired(string value, string errorMessage)
    {
        return SandboxActivityMetadata.NormalizeRequired(value, errorMessage);
    }

    static Proto.SandboxActivityImage BuildImage(SandboxWorkerProfileOptions options)
    {
        string imageRef = NormalizeRequired(
            options.ContainerImage ?? string.Empty,
            "On-demand sandbox activity image metadata requires a container image reference like 'myregistry.azurecr.io/workers/hello:1.0' or 'myregistry.azurecr.io/workers/hello@sha256:...'.");

        Proto.SandboxActivityImage image = new()
        {
            ImageRef = imageRef,
            ManagedIdentityClientId = NormalizeRequired(
                options.ImagePullManagedIdentityClientId ?? string.Empty,
                "On-demand sandbox activity workerProfile requires the managed identity client ID ADC uses to pull the worker image."),
        };

        return image;
    }

    static Proto.SandboxActivity ToProtoActivity(SandboxActivityMetadata.Activity activity) => new()
    {
        Name = activity.Name,
        Version = activity.Version ?? string.Empty,
    };

    static Proto.SandboxActivityResources BuildResources(SandboxWorkerProfileOptions options)
    {
        string cpu = NormalizeCpu(options.Cpu, out long cpuMillicores);
        string memory = NormalizeMemory(options.Memory, cpuMillicores);

        return new Proto.SandboxActivityResources
        {
            Cpu = cpu,
            Memory = memory,
        };
    }

    static string NormalizeCpu(string value, out long cpuMillicores)
    {
        string normalized = NormalizeRequired(value, "On-demand sandbox activity workerProfile requires CPU resources.");
        if (TryParseCpuMillicores(normalized) is not { } milliCpu
            || milliCpu < MinCpuMillicores
            || milliCpu > MaxCpuMillicores
            || milliCpu % CpuStepMillicores != 0)
        {
            throw new InvalidOperationException(
                "On-demand sandbox activity CPU resources must match an ADC sandbox CPU tier: " +
                "250m through 16000m, in 250m increments. Use formats like '500m', '2', or '0.5'.");
        }

        cpuMillicores = milliCpu;
        return normalized;
    }

    static string NormalizeMemory(string value, long cpuMillicores)
    {
        string normalized = NormalizeRequired(value, "On-demand sandbox activity workerProfile requires memory resources.");
        long maxMemoryMiB = GetMaxMemoryMiB(cpuMillicores);
        if (TryParseMemoryMiB(normalized) is not { } memoryMiB || memoryMiB <= 0)
        {
            throw new InvalidOperationException(
                "On-demand sandbox activity memory resources must be a positive Kubernetes-style memory quantity. " +
                "Use formats like '256Mi', '1Gi', or '2048'.");
        }

        if (memoryMiB > maxMemoryMiB)
        {
            throw new InvalidOperationException(
                "On-demand sandbox activity memory resources exceed the ADC sandbox tier maximum for the configured CPU. " +
                $"Maximum memory for CPU '{cpuMillicores}m' is {maxMemoryMiB}Mi.");
        }

        return normalized;
    }

    static long? TryParseCpuMillicores(string value)
    {
        if (value.EndsWith('m'))
        {
            return long.TryParse(
                value[..^1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long milliCpu)
                ? milliCpu
                : null;
        }

        return decimal.TryParse(
            value,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out decimal cores)
            ? TryConvertCoresToMillicores(cores)
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
                ? TryConvertMemoryToMiB(gib, 1024)
                : null;
        }

        if (value.EndsWith("Mi", StringComparison.OrdinalIgnoreCase))
        {
            return decimal.TryParse(
                value[..^2],
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal mib)
                ? TryConvertMemoryToMiB(mib, 1)
                : null;
        }

        return decimal.TryParse(
            value,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out decimal bareMiB)
            ? TryConvertMemoryToMiB(bareMiB, 1)
            : null;
    }

    static long? TryConvertCoresToMillicores(decimal cores)
    {
        if (cores < 0 || cores > MaxCpuMillicores / 1000m)
        {
            return null;
        }

        decimal millicores = cores * 1000;
        return millicores == decimal.Truncate(millicores) ? (long)millicores : null;
    }

    static long? TryConvertMemoryToMiB(decimal value, decimal multiplier)
    {
        if (value < 0 || value > long.MaxValue / multiplier)
        {
            return null;
        }

        return (long)(value * multiplier);
    }

    static long GetMaxMemoryMiB(long cpuMillicores)
    {
        return cpuMillicores * MemoryMiBPerCore / 1000;
    }

    static string[] NormalizeOptionalStrings(IEnumerable<string> values)
    {
        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToArray();
    }
}
