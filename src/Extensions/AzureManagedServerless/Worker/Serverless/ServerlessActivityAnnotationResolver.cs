// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using System.Threading;

namespace Microsoft.DurableTask.Worker.AzureManaged.Serverless;

/// <summary>
/// Resolves serverless worker profile and activity annotations from loaded assemblies.
/// </summary>
static class ServerlessActivityAnnotationResolver
{
    static readonly Lazy<AnnotationCatalog> Catalog = new(ScanAnnotations, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Resolves annotated serverless declarations for the specified task hub.
    /// </summary>
    /// <param name="taskHub">The task hub name.</param>
    /// <returns>The resolved serverless declaration options.</returns>
    public static IReadOnlyList<ServerlessOptions> ResolveDeclarations(string taskHub)
    {
        string normalizedTaskHub = string.IsNullOrWhiteSpace(taskHub)
            ? throw new InvalidOperationException("Serverless activity declaration requires a task hub name.")
            : taskHub.Trim();

        AnnotationCatalog catalog = Catalog.Value;
        Dictionary<string, ServerlessOptions> optionsByProfile = new(StringComparer.Ordinal);
        foreach (ActivityMetadata activity in catalog.Activities)
        {
            if (!optionsByProfile.TryGetValue(activity.WorkerProfileId, out ServerlessOptions? options))
            {
                ProfileMetadata profile = catalog.Profiles[activity.WorkerProfileId];
                options = CreateOptions(normalizedTaskHub, profile);
                optionsByProfile.Add(activity.WorkerProfileId, options);
            }

            options.ActivityNames.Add(activity.ActivityName);
        }

        return optionsByProfile.Values.ToArray();
    }

    /// <summary>
    /// Resolves annotated serverless activity names.
    /// </summary>
    /// <returns>The resolved activity names.</returns>
    public static string[] ResolveActivityNames()
    {
        return Catalog.Value.Activities
            .Select(static activity => activity.ActivityName)
            .ToArray();
    }

    static AnnotationCatalog ScanAnnotations()
    {
        Dictionary<string, ProfileMetadata> profiles = new(StringComparer.Ordinal);
        List<ActivityMetadata> activities = [];
        Dictionary<string, string> activityOwners = new(StringComparer.Ordinal);
        List<(Type Type, ServerlessActivityAttribute Attribute)> activityAnnotations = [];

        foreach (Type type in GetCandidateTypes())
        {
            if (type.GetCustomAttribute<ServerlessWorkerProfileAttribute>() is { } profile)
            {
                if (profiles.ContainsKey(profile.WorkerProfileId))
                {
                    throw new InvalidOperationException($"Serverless worker profile '{profile.WorkerProfileId}' is declared more than once.");
                }

                profiles.Add(profile.WorkerProfileId, new ProfileMetadata(profile.WorkerProfileId, type));
            }

            if (type.GetCustomAttribute<ServerlessActivityAttribute>() is { } activity)
            {
                activityAnnotations.Add((type, activity));
            }
        }

        foreach ((Type type, ServerlessActivityAttribute activity) in activityAnnotations)
        {
            if (!profiles.ContainsKey(activity.WorkerProfileId))
            {
                throw new InvalidOperationException($"Serverless activity '{type.FullName}' references undeclared worker profile '{activity.WorkerProfileId}'.");
            }

            string activityName = GetActivityName(type, activity);
            if (activityOwners.TryGetValue(activityName, out string? existingProfile))
            {
                throw new InvalidOperationException($"Serverless activity '{activityName}' is assigned to both worker profile '{existingProfile}' and '{activity.WorkerProfileId}'.");
            }

            activityOwners.Add(activityName, activity.WorkerProfileId);
            activities.Add(new ActivityMetadata(activityName, activity.WorkerProfileId, type));
        }

        return new AnnotationCatalog(profiles, activities);
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

    static string GetActivityName(Type type, ServerlessActivityAttribute activity)
    {
        Check.NotNull(type);
        if (!string.IsNullOrWhiteSpace(activity.Name))
        {
            return activity.Name.Trim();
        }

        if (!typeof(ITaskActivity).IsAssignableFrom(type))
        {
            throw new InvalidOperationException($"Serverless activity declaration marker '{type.FullName}' must specify {nameof(ServerlessActivityAttribute.Name)} or implement {nameof(ITaskActivity)}.");
        }

        return ServerlessTaskNameResolver.GetTaskName(type);
    }

    sealed record AnnotationCatalog(
        IReadOnlyDictionary<string, ProfileMetadata> Profiles,
        IReadOnlyList<ActivityMetadata> Activities);

    sealed record ProfileMetadata(
        string WorkerProfileId,
        Type Type);

    sealed record ActivityMetadata(
        string ActivityName,
        string WorkerProfileId,
        Type Type);
}
