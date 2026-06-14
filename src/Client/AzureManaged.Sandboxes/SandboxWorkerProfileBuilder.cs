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
    /// <summary>
    /// Resolves configured activity names for on-demand sandbox activity execution.
    /// </summary>
    /// <param name="configuredNames">The configured activity names.</param>
    /// <returns>The normalized activity names.</returns>
    public static string[] ResolveActivityNames(IEnumerable<string> configuredNames)
    {
        return SandboxActivityMetadata.ResolveActivityNames(configuredNames);
    }

    /// <summary>
    /// Builds an on-demand sandbox activity workerProfile protocol message.
    /// </summary>
    /// <param name="options">The on-demand sandbox options.</param>
    /// <param name="activityNames">The activity names included in the workerProfile.</param>
    /// <returns>The workerProfile protocol message.</returns>
    public static Proto.SandboxWorkerProfile BuildWorkerProfile(
        SandboxWorkerProfileOptions options,
        IReadOnlyCollection<string> activityNames)
    {
        Check.NotNull(options);
        Check.NotNull(activityNames);

        _ = NormalizeRequired(options.TaskHub, "On-demand sandbox activity workerProfile requires a task hub name.");
        if (activityNames.Count == 0)
        {
            throw new InvalidOperationException("On-demand sandbox activity workerProfile requires at least one activity name.");
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

        workerProfile.ActivityNames.AddRange(activityNames);
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

    static Proto.SandboxActivityResources BuildResources(SandboxWorkerProfileOptions options)
    {
        string cpu = NormalizeCpu(options.Cpu);
        string memory = NormalizeMemory(options.Memory);

        return new Proto.SandboxActivityResources
        {
            Cpu = cpu,
            Memory = memory,
        };
    }

    static string NormalizeCpu(string value)
    {
        string normalized = NormalizeRequired(value, "On-demand sandbox activity workerProfile requires CPU resources.");
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
        string normalized = NormalizeRequired(value, "On-demand sandbox activity workerProfile requires memory resources.");
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
