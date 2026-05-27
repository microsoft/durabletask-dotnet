// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using System.Threading;

namespace Microsoft.DurableTask.Worker.AzureManaged.Serverless;

/// <summary>
/// Resolves serverless activity declarations from worker profile annotations.
/// </summary>
static class ServerlessActivityDeclarationResolver
{
    static readonly Lazy<ProfileMetadata[]> Profiles = new(ScanProfiles, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Resolves serverless declarations for the specified task hub.
    /// </summary>
    /// <param name="taskHub">The task hub name.</param>
    /// <returns>The resolved serverless declaration options.</returns>
    public static IReadOnlyList<ServerlessOptions> ResolveDeclarations(string taskHub)
    {
        string normalizedTaskHub = string.IsNullOrWhiteSpace(taskHub)
            ? throw new InvalidOperationException("Serverless activity declaration requires a task hub name.")
            : taskHub.Trim();

        ServerlessOptions[] declarations = Profiles.Value
            .Select(profile => CreateOptions(normalizedTaskHub, profile))
            .Where(static options => ServerlessActivityConfiguration.ResolveActivityNames(options.ActivityNames).Length > 0)
            .ToArray();

        ValidateActivityOwnership(declarations);
        return declarations;
    }

    /// <summary>
    /// Resolves activity names declared by serverless worker profiles.
    /// </summary>
    /// <param name="taskHub">The task hub name.</param>
    /// <returns>The resolved activity names.</returns>
    public static string[] ResolveDeclaredActivityNames(string taskHub)
    {
        return ResolveDeclarations(taskHub)
            .SelectMany(static options => ServerlessActivityConfiguration.ResolveActivityNames(options.ActivityNames))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    static ProfileMetadata[] ScanProfiles()
    {
        Dictionary<string, ProfileMetadata> profiles = new(StringComparer.Ordinal);
        foreach (Type type in GetCandidateTypes())
        {
            if (type.GetCustomAttribute<ServerlessWorkerProfileAttribute>() is not { } profile)
            {
                continue;
            }

            if (profiles.ContainsKey(profile.WorkerProfileId))
            {
                throw new InvalidOperationException($"Serverless worker profile '{profile.WorkerProfileId}' is declared more than once.");
            }

            profiles.Add(profile.WorkerProfileId, new ProfileMetadata(profile.WorkerProfileId, type));
        }

        return profiles.Values.ToArray();
    }

    static ServerlessOptions CreateOptions(string taskHub, ProfileMetadata profile)
    {
        ServerlessOptions options = new()
        {
            TaskHub = taskHub,
            WorkerProfileId = profile.WorkerProfileId,
        };

        ConfigureProfile(profile.Type, options);

        return options;
    }

    static void ConfigureProfile(Type profileType, ServerlessOptions options)
    {
        if (!typeof(IServerlessWorkerProfile).IsAssignableFrom(profileType))
        {
            return;
        }

        object? instance = Activator.CreateInstance(profileType, nonPublic: true)
            ?? throw new InvalidOperationException($"Serverless worker profile '{profileType.FullName}' could not be created.");
        ((IServerlessWorkerProfile)instance).Configure(options);
    }

    static IEnumerable<Type> GetCandidateTypes()
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(static type => type is not null).Cast<Type>().ToArray();
            }

            foreach (Type type in types)
            {
                yield return type;
            }
        }
    }

    static void ValidateActivityOwnership(IEnumerable<ServerlessOptions> declarations)
    {
        Dictionary<string, string> activityOwners = new(StringComparer.Ordinal);
        foreach (ServerlessOptions declaration in declarations)
        {
            foreach (string activityName in ServerlessActivityConfiguration.ResolveActivityNames(declaration.ActivityNames))
            {
                if (activityOwners.TryGetValue(activityName, out string? existingProfile)
                    && !string.Equals(existingProfile, declaration.WorkerProfileId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Serverless activity '{activityName}' is assigned to both worker profile '{existingProfile}' and '{declaration.WorkerProfileId}'.");
                }

                activityOwners[activityName] = declaration.WorkerProfileId;
            }
        }
    }

    sealed record ProfileMetadata(
        string WorkerProfileId,
        Type Type);
}
