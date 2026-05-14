// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Proto = Microsoft.DurableTask.Protobuf.Serverless;

namespace Microsoft.DurableTask.Worker.AzureManaged.Serverless;

/// <summary>
/// Builds and normalizes remote activity protocol messages.
/// </summary>
static class RemoteActivityConfiguration
{
    /// <summary>
    /// Resolves configured activity names for a remote activity worker.
    /// </summary>
    /// <param name="configuredNames">The configured activity names.</param>
    /// <returns>The normalized activity names.</returns>
    public static string[] ResolveActivityNames(ICollection<string> configuredNames)
    {
        return configuredNames
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Builds a remote activity declaration protocol message.
    /// </summary>
    /// <param name="options">The declaration options.</param>
    /// <param name="activityNames">The activity names included in the declaration.</param>
    /// <returns>The declaration protocol message.</returns>
    public static Proto.RemoteActivityDeclaration BuildDeclaration(RemoteActivityOptions options, IReadOnlyCollection<string> activityNames)
    {
        Check.NotNull(options);
        Check.NotNull(activityNames);

        if (string.IsNullOrWhiteSpace(options.TaskHub))
        {
            throw new InvalidOperationException("Remote activity declaration requires a task hub name.");
        }

        if (activityNames.Count == 0)
        {
            throw new InvalidOperationException("Remote activity declaration requires at least one activity name.");
        }

        if (options.MaxConcurrentActivities <= 0)
        {
            throw new InvalidOperationException("Remote activity max concurrent activities must be greater than zero.");
        }

        Proto.RemoteActivityDeclaration declaration = new()
        {
            TaskHub = options.TaskHub,
            WorkerProfileId = RemoteActivityOptions.DefaultWorkerProfileId,
            Image = BuildImage(options),
            MaxConcurrentActivities = options.MaxConcurrentActivities,
        };

        declaration.ActivityNames.AddRange(activityNames);
        declaration.EnvironmentVariables.Add(options.EnvironmentVariables);
        return declaration;
    }

    /// <summary>
    /// Builds the initial remote activity worker registration message.
    /// </summary>
    /// <param name="options">The worker options.</param>
    /// <returns>The worker start protocol message.</returns>
    public static Proto.RemoteActivityWorkerMessage BuildWorkerStart(RemoteActivityWorkerOptions options)
    {
        Check.NotNull(options);

        if (string.IsNullOrWhiteSpace(options.TaskHub))
        {
            throw new InvalidOperationException("Remote activity worker registration requires a task hub name.");
        }

        if (options.MaxConcurrentActivities <= 0)
        {
            throw new InvalidOperationException("Remote activity worker max concurrent activities must be greater than zero.");
        }

        Proto.RemoteActivityWorkerStart start = new()
        {
            TaskHub = options.TaskHub,
            WorkerInstanceId = options.WorkerInstanceId,
            MaxActivitiesCount = options.MaxConcurrentActivities,
            Substrate = GetSubstrateFromEnvironment(),
            SandboxId = Environment.GetEnvironmentVariable("DTS_SANDBOX_ID") ?? string.Empty,
        };

        return new Proto.RemoteActivityWorkerMessage { Start = start };
    }

    /// <summary>
    /// Builds a remote activity worker heartbeat message.
    /// </summary>
    /// <param name="activeActivitiesCount">The number of activities currently executing.</param>
    /// <returns>The heartbeat protocol message.</returns>
    public static Proto.RemoteActivityWorkerMessage BuildWorkerHeartbeat(int activeActivitiesCount)
    {
        if (activeActivitiesCount < 0)
        {
            throw new InvalidOperationException("Remote activity worker active activity count cannot be negative.");
        }

        return new Proto.RemoteActivityWorkerMessage
        {
            Heartbeat = new Proto.RemoteActivityWorkerHeartbeat
            {
                ActiveActivitiesCount = activeActivitiesCount,
            },
        };
    }

    static Proto.RemoteActivityImage BuildImage(RemoteActivityOptions options)
    {
        if (!options.PublicPull)
        {
            throw new InvalidOperationException("Remote activity images must be publicly pullable for private preview.");
        }

        string? imageRef = Coalesce(
            options.ContainerImage,
            BuildImageRef(options.RegistryServer, options.Repository, options.Tag, options.ImageDigest));

        if (string.IsNullOrWhiteSpace(imageRef))
        {
            throw new InvalidOperationException("Remote activity image metadata requires a container image reference.");
        }

        return new Proto.RemoteActivityImage
        {
            ImageRef = imageRef,
            PublicPull = true,
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

    static string? BuildImageRef(string? registryServer, string? repository, string? tag, string? digest)
    {
        if (string.IsNullOrWhiteSpace(repository))
        {
            return null;
        }

        string image = string.IsNullOrWhiteSpace(registryServer) ? repository : $"{registryServer}/{repository}";
        if (!string.IsNullOrWhiteSpace(digest))
        {
            return $"{image}@{digest}";
        }

        return string.IsNullOrWhiteSpace(tag) ? image : $"{image}:{tag}";
    }

    static string? Coalesce(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
