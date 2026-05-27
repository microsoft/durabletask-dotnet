// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;

namespace Microsoft.DurableTask.Worker.AzureManaged.Serverless;

/// <summary>
/// Resolves serverless worker profile and activity annotations from loaded assemblies.
/// </summary>
static class ServerlessActivityAnnotationResolver
{
    /// <summary>
    /// Resolves annotated serverless declarations for the specified task hub.
    /// </summary>
    /// <param name="taskHub">The task hub name.</param>
    /// <returns>The resolved serverless declaration options.</returns>
    public static IReadOnlyList<ServerlessOptions> Resolve(string taskHub)
    {
        string normalizedTaskHub = string.IsNullOrWhiteSpace(taskHub)
            ? throw new InvalidOperationException("Serverless activity declaration requires a task hub name.")
            : taskHub.Trim();

        Dictionary<string, ServerlessOptions> profiles = new(StringComparer.Ordinal);
        Dictionary<string, string> activityOwners = new(StringComparer.Ordinal);

        foreach (Type type in GetCandidateTypes())
        {
            if (type.GetCustomAttribute<ServerlessWorkerProfileAttribute>() is { } profile)
            {
                if (profiles.ContainsKey(profile.WorkerProfileId))
                {
                    throw new InvalidOperationException($"Serverless worker profile '{profile.WorkerProfileId}' is declared more than once.");
                }

                profiles.Add(profile.WorkerProfileId, CreateOptions(normalizedTaskHub, profile, type));
            }
        }

        foreach (Type type in GetCandidateTypes())
        {
            if (type.GetCustomAttribute<ServerlessActivityAttribute>() is not { } activity)
            {
                continue;
            }

            if (!profiles.TryGetValue(activity.WorkerProfileId, out ServerlessOptions? options))
            {
                throw new InvalidOperationException($"Serverless activity '{type.FullName}' references undeclared worker profile '{activity.WorkerProfileId}'.");
            }

            string activityName = GetTaskName(type, activity);
            if (activityOwners.TryGetValue(activityName, out string? existingProfile))
            {
                throw new InvalidOperationException($"Serverless activity '{activityName}' is assigned to both worker profile '{existingProfile}' and '{activity.WorkerProfileId}'.");
            }

            activityOwners.Add(activityName, activity.WorkerProfileId);
            options.ActivityNames.Add(activityName);
        }

        return profiles.Values
            .Where(static options => options.ActivityNames.Count > 0)
            .ToArray();
    }

    /// <summary>
    /// Resolves annotated serverless activity names.
    /// </summary>
    /// <returns>The resolved activity names.</returns>
    public static string[] ResolveActivityNames() => Resolve(taskHub: "annotation-scan")
        .SelectMany(static options => options.ActivityNames)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    static ServerlessOptions CreateOptions(string taskHub, ServerlessWorkerProfileAttribute profile, Type profileType)
    {
        ServerlessOptions options = new()
        {
            TaskHub = taskHub,
            WorkerProfileId = profile.WorkerProfileId,
        };

        ConfigureProfile(profileType, options);

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

    static string GetTaskName(Type type, ServerlessActivityAttribute activity)
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

        return Attribute.GetCustomAttribute(type, typeof(DurableTaskAttribute)) is DurableTaskAttribute { Name.Name: not null and not "" } attr
            ? attr.Name.Name
            : type.Name;
    }
}
