// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using System.Threading;

namespace Microsoft.DurableTask.Worker.AzureManaged.OnDemandSandbox;

/// <summary>
/// Resolves on-demand sandbox activity declarations from worker profile configuration.
/// </summary>
static class OnDemandSandboxActivityDeclarationResolver
{
    static readonly Lazy<ProfileMetadata[]> Profiles = new(ScanProfiles, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Resolves on-demand sandbox declarations for the specified task hub.
    /// </summary>
    /// <param name="taskHub">The task hub name.</param>
    /// <returns>The resolved on-demand sandbox declaration options.</returns>
    public static IReadOnlyList<OnDemandSandboxOptions> ResolveDeclarations(string taskHub)
    {
        string normalizedTaskHub = string.IsNullOrWhiteSpace(taskHub)
            ? throw new InvalidOperationException("On-demand sandbox activity declaration requires a task hub name.")
            : taskHub.Trim();

        OnDemandSandboxOptions[] declarations = Profiles.Value
            .Select(profile => CreateOptions(normalizedTaskHub, profile))
            .Where(static options => OnDemandSandboxActivityConfiguration.ResolveActivityNames(options.ActivityNames).Length > 0)
            .ToArray();

        ValidateActivityOwnership(declarations);
        return declarations;
    }

    static ProfileMetadata[] ScanProfiles()
    {
        Dictionary<string, ProfileMetadata> profiles = new(StringComparer.Ordinal);
        foreach (Type type in GetCandidateTypes())
        {
            if (type.GetCustomAttribute<OnDemandSandboxWorkerProfileAttribute>() is not { } profile)
            {
                continue;
            }

            if (profiles.ContainsKey(profile.WorkerProfileId))
            {
                throw new InvalidOperationException($"On-demand sandbox worker profile '{profile.WorkerProfileId}' is declared more than once.");
            }

            profiles.Add(profile.WorkerProfileId, new ProfileMetadata(profile.WorkerProfileId, type));
        }

        return profiles.Values.ToArray();
    }

    static OnDemandSandboxOptions CreateOptions(
        string taskHub,
        ProfileMetadata profile)
    {
        OnDemandSandboxOptions options = new()
        {
            TaskHub = taskHub,
            WorkerProfileId = profile.WorkerProfileId,
        };

        ConfigureProfile(profile.Type, options);
        return options;
    }

    static void ConfigureProfile(Type profileType, OnDemandSandboxOptions options)
    {
        if (!typeof(ISandboxWorkerProfile).IsAssignableFrom(profileType))
        {
            return;
        }

        object? instance = Activator.CreateInstance(profileType, nonPublic: true)
            ?? throw new InvalidOperationException($"On-demand sandbox worker profile '{profileType.FullName}' could not be created.");
        ((ISandboxWorkerProfile)instance).Configure(options);
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

    static void ValidateActivityOwnership(IEnumerable<OnDemandSandboxOptions> declarations)
    {
        Dictionary<string, string> activityOwners = new(StringComparer.Ordinal);
        foreach (OnDemandSandboxOptions declaration in declarations)
        {
            foreach (string activityName in OnDemandSandboxActivityConfiguration.ResolveActivityNames(declaration.ActivityNames))
            {
                if (activityOwners.TryGetValue(activityName, out string? existingProfile)
                    && !string.Equals(existingProfile, declaration.WorkerProfileId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"On-demand sandbox activity '{activityName}' is assigned to both worker profile '{existingProfile}' and '{declaration.WorkerProfileId}'.");
                }

                activityOwners[activityName] = declaration.WorkerProfileId;
            }
        }
    }

    sealed record ProfileMetadata(
        string WorkerProfileId,
        Type Type);
}
