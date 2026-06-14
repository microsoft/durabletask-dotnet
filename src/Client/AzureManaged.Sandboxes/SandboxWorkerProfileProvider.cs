// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using System.Threading;

namespace Microsoft.DurableTask.Client.AzureManaged;

/// <summary>
/// Provides on-demand sandbox activity workerProfiles from worker profile configuration.
/// </summary>
sealed class SandboxWorkerProfileProvider
{
    readonly Lazy<ProfileMetadata[]> profiles;

    /// <summary>
    /// Initializes a new instance of the <see cref="SandboxWorkerProfileProvider"/> class.
    /// </summary>
    public SandboxWorkerProfileProvider()
    {
        this.profiles = new Lazy<ProfileMetadata[]>(ScanProfiles, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Resolves on-demand sandbox workerProfiles for the specified task hub.
    /// </summary>
    /// <param name="taskHub">The task hub name.</param>
    /// <returns>The resolved on-demand sandbox workerProfile options.</returns>
    public IReadOnlyList<SandboxWorkerProfileOptions> ResolveWorkerProfiles(string taskHub)
    {
        string normalizedTaskHub = string.IsNullOrWhiteSpace(taskHub)
            ? throw new InvalidOperationException("On-demand sandbox activity workerProfile requires a task hub name.")
            : taskHub.Trim();

        SandboxWorkerProfileOptions[] workerProfiles = this.profiles.Value
            .Select(profile => CreateOptions(normalizedTaskHub, profile))
            .Where(static options => SandboxWorkerProfileBuilder.ResolveActivityNames(options.ActivityNames).Length > 0)
            .ToArray();

        ValidateActivityOwnership(workerProfiles);
        return workerProfiles;
    }

    /// <summary>
    /// Validates that a profile type can configure on-demand sandbox workerProfiles.
    /// </summary>
    /// <param name="profileType">The profile type.</param>
    internal static void ValidateProfileType(Type profileType)
    {
        if (!typeof(ISandboxWorkerProfile).IsAssignableFrom(profileType))
        {
            throw new InvalidOperationException(
                $"On-demand sandbox worker profile '{profileType.FullName}' must implement {nameof(ISandboxWorkerProfile)}.");
        }
    }

    static ProfileMetadata[] ScanProfiles()
    {
        Dictionary<string, ProfileMetadata> profiles = new(StringComparer.Ordinal);
        foreach (Type type in GetCandidateTypes())
        {
            if (type.GetCustomAttribute<SandboxWorkerProfileAttribute>() is not { } profile)
            {
                continue;
            }

            ValidateProfileType(type);

            if (profiles.ContainsKey(profile.WorkerProfileId))
            {
                throw new InvalidOperationException($"On-demand sandbox worker profile '{profile.WorkerProfileId}' is declared more than once.");
            }

            profiles.Add(profile.WorkerProfileId, new ProfileMetadata(profile.WorkerProfileId, type));
        }

        return profiles.Values.ToArray();
    }

    static SandboxWorkerProfileOptions CreateOptions(
        string taskHub,
        ProfileMetadata profile)
    {
        SandboxWorkerProfileOptions options = new()
        {
            TaskHub = taskHub,
            WorkerProfileId = profile.WorkerProfileId,
        };

        ConfigureProfile(profile.Type, options);
        return options;
    }

    static void ConfigureProfile(Type profileType, SandboxWorkerProfileOptions options)
    {
        ValidateProfileType(profileType);

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

    static void ValidateActivityOwnership(IEnumerable<SandboxWorkerProfileOptions> workerProfiles)
    {
        Dictionary<string, string> activityOwners = new(StringComparer.OrdinalIgnoreCase);
        foreach (SandboxWorkerProfileOptions workerProfile in workerProfiles)
        {
            foreach (string activityName in SandboxWorkerProfileBuilder.ResolveActivityNames(workerProfile.ActivityNames))
            {
                if (activityOwners.TryGetValue(activityName, out string? existingProfile)
                    && !string.Equals(existingProfile, workerProfile.WorkerProfileId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"On-demand sandbox activity '{activityName}' is assigned to both worker profile '{existingProfile}' and '{workerProfile.WorkerProfileId}'.");
                }

                activityOwners[activityName] = workerProfile.WorkerProfileId;
            }
        }
    }

    sealed record ProfileMetadata(
        string WorkerProfileId,
        Type Type);
}
