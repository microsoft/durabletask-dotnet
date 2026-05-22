// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Proto = Microsoft.DurableTask.Protobuf.Serverless;

namespace Microsoft.DurableTask.Worker.AzureManaged.Serverless;

/// <summary>
/// Builds and normalizes serverless activity protocol messages.
/// </summary>
static class ServerlessActivityConfiguration
{
    /// <summary>
    /// Resolves configured activity names for serverless activity execution.
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
    /// Builds a serverless activity declaration protocol message.
    /// </summary>
    /// <param name="options">The serverless options.</param>
    /// <param name="activityNames">The activity names included in the declaration.</param>
    /// <returns>The declaration protocol message.</returns>
    public static Proto.ServerlessActivityDeclaration BuildDeclaration(ServerlessOptions options, IReadOnlyCollection<string> activityNames)
    {
        Check.NotNull(options);
        Check.NotNull(activityNames);

        ValidateTaskHub(options.TaskHub, "Serverless activity declaration requires a task hub name.");

        if (activityNames.Count == 0)
        {
            throw new InvalidOperationException("Serverless activity declaration requires at least one activity name.");
        }

        string workerProfileId = NormalizeWorkerProfileId(options.WorkerProfileId, "Serverless activity declaration requires a worker profile ID.");

        if (options.MaxConcurrentActivities <= 0)
        {
            throw new InvalidOperationException("Serverless activity max concurrent activities must be greater than zero.");
        }

        Proto.ServerlessActivityDeclaration declaration = new()
        {
            WorkerProfileId = workerProfileId,
            Image = BuildImage(options),
            Resources = BuildResources(options),
            MaxConcurrentActivities = options.MaxConcurrentActivities,
        };

        declaration.ActivityNames.AddRange(activityNames);
        declaration.EnvironmentVariables.Add(options.EnvironmentVariables);
        declaration.Entrypoint.AddRange(NormalizeOptionalStrings(options.Entrypoint));
        declaration.Cmd.AddRange(NormalizeOptionalStrings(options.Cmd));
        return declaration;
    }

    /// <summary>
    /// Builds the initial serverless activity worker registration message.
    /// </summary>
    /// <param name="options">The serverless options.</param>
    /// <returns>The worker start protocol message.</returns>
    public static Proto.ServerlessActivityWorkerMessage BuildWorkerStart(ServerlessOptions options)
    {
        Check.NotNull(options);

        ValidateTaskHub(options.TaskHub, "Serverless activity worker registration requires a task hub name.");

        if (options.MaxConcurrentActivities <= 0)
        {
            throw new InvalidOperationException("Serverless activity worker max concurrent activities must be greater than zero.");
        }

        string workerProfileId = NormalizeWorkerProfileId(options.WorkerProfileId, "Serverless activity worker registration requires a worker profile ID.");

        Proto.ServerlessActivityWorkerStart start = new()
        {
            TaskHub = options.TaskHub,
            WorkerProfileId = workerProfileId,
            MaxActivitiesCount = options.MaxConcurrentActivities,
            Substrate = GetSubstrateFromEnvironment(),
            DtsSandboxIdentifier = Environment.GetEnvironmentVariable("DTS_SANDBOX_ID") ?? string.Empty,
        };

        return new Proto.ServerlessActivityWorkerMessage { Start = start };
    }

    /// <summary>
    /// Builds a serverless activity worker heartbeat message.
    /// </summary>
    /// <param name="activeActivitiesCount">The number of activities currently executing.</param>
    /// <returns>The heartbeat protocol message.</returns>
    public static Proto.ServerlessActivityWorkerMessage BuildWorkerHeartbeat(int activeActivitiesCount)
    {
        if (activeActivitiesCount < 0)
        {
            throw new InvalidOperationException("Serverless activity worker active activity count cannot be negative.");
        }

        return new Proto.ServerlessActivityWorkerMessage
        {
            Heartbeat = new Proto.ServerlessActivityWorkerHeartbeat
            {
                ActiveActivitiesCount = activeActivitiesCount,
            },
        };
    }

    static Proto.ServerlessActivityImage BuildImage(ServerlessOptions options)
    {
        if (!options.PublicPull)
        {
            throw new InvalidOperationException("Serverless activity images must be publicly pullable for private preview.");
        }

        string? imageRef = Coalesce(
            options.ContainerImage,
            BuildImageRef(options.RegistryServer, options.Repository, options.Tag, options.ImageDigest));

        if (string.IsNullOrWhiteSpace(imageRef))
        {
            throw new InvalidOperationException("Serverless activity image metadata requires a container image reference.");
        }

        return new Proto.ServerlessActivityImage
        {
            ImageRef = imageRef,
            PublicPull = true,
        };
    }

    static Proto.ServerlessActivityResources BuildResources(ServerlessOptions options)
    {
        string cpu = NormalizeRequired(options.Cpu, "Serverless activity declaration requires CPU resources.");
        string memory = NormalizeRequired(options.Memory, "Serverless activity declaration requires memory resources.");

        return new Proto.ServerlessActivityResources
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

    static string[] NormalizeOptionalStrings(IEnumerable<string> values)
    {
        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToArray();
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
