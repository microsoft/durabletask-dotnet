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
    static readonly Lazy<ActivityMetadata[]> Activities = new(ScanActivities, LazyThreadSafetyMode.ExecutionAndPublication);

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

        ProfileMetadata[] profiles = Profiles.Value;
        Dictionary<string, string[]> activitiesByProfile = ResolveActivitiesByProfile(profiles);

        ServerlessOptions[] declarations = profiles
            .Select(profile => CreateOptions(normalizedTaskHub, profile, activitiesByProfile))
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

    static ActivityMetadata[] ScanActivities()
    {
        List<ActivityMetadata> activities = [];
        foreach (Type type in GetCandidateTypes())
        {
            if (type.GetCustomAttribute<ServerlessActivityAttribute>() is not { } activity)
            {
                continue;
            }

            activities.Add(new ActivityMetadata(
                ResolveActivityName(type, activity),
                ResolveWorkerProfileId(activity)));
        }

        return activities.ToArray();
    }

    static Dictionary<string, string[]> ResolveActivitiesByProfile(IReadOnlyCollection<ProfileMetadata> profiles)
    {
        Dictionary<string, string[]> activitiesByProfile = Activities.Value
            .GroupBy(static activity => activity.WorkerProfileId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(static activity => activity.Name)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        HashSet<string> profileIds = profiles
            .Select(static profile => profile.WorkerProfileId)
            .ToHashSet(StringComparer.Ordinal);
        string[] missingProfiles = activitiesByProfile.Keys
            .Where(profileId => !profileIds.Contains(profileId))
            .ToArray();
        if (missingProfiles.Length > 0)
        {
            throw new InvalidOperationException($"Serverless worker profile '{missingProfiles[0]}' is referenced by a serverless activity but is not declared.");
        }

        return activitiesByProfile;
    }

    static ServerlessOptions CreateOptions(
        string taskHub,
        ProfileMetadata profile,
        Dictionary<string, string[]> activitiesByProfile)
    {
        ServerlessOptions options = new()
        {
            TaskHub = taskHub,
            WorkerProfileId = profile.WorkerProfileId,
        };

        ConfigureProfile(profile.Type, options);
        if (activitiesByProfile.TryGetValue(profile.WorkerProfileId, out string[]? activityNames))
        {
            foreach (string activityName in activityNames)
            {
                options.ActivityNames.Add(activityName);
            }
        }

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

    static string ResolveActivityName(Type type, ServerlessActivityAttribute activity)
    {
        string activityName = string.IsNullOrWhiteSpace(activity.Name)
            ? type.Name
            : activity.Name.Trim();

        return string.IsNullOrWhiteSpace(activityName)
            ? throw new InvalidOperationException($"Serverless activity declaration '{type.FullName}' requires an activity name.")
            : activityName;
    }

    static string ResolveWorkerProfileId(ServerlessActivityAttribute activity)
    {
        return string.IsNullOrWhiteSpace(activity.WorkerProfile)
            ? throw new InvalidOperationException("Serverless activity declaration requires a worker profile ID.")
            : activity.WorkerProfile.Trim();
    }

    sealed record ProfileMetadata(
        string WorkerProfileId,
        Type Type);

    sealed record ActivityMetadata(
        string Name,
        string WorkerProfileId);
}
