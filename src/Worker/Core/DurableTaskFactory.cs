// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// A factory for creating orchestrators and activities.
/// </summary>
sealed class DurableTaskFactory : IDurableTaskFactory2, IVersionedActivityFactory, IVersionedOrchestratorFactory
{
    readonly IDictionary<ActivityVersionKey, Func<IServiceProvider, ITaskActivity>> activities;
    readonly IDictionary<OrchestratorVersionKey, Func<IServiceProvider, ITaskOrchestrator>> orchestrators;
    readonly IDictionary<TaskName, Func<IServiceProvider, ITaskEntity>> entities;
    readonly HashSet<string> versionedOrchestratorNames;
    readonly HashSet<string> versionedActivityNames;

    /// <summary>
    /// Initializes a new instance of the <see cref="DurableTaskFactory" /> class.
    /// </summary>
    /// <param name="activities">The activity factories.</param>
    /// <param name="orchestrators">The orchestrator factories.</param>
    /// <param name="entities">The entity factories.</param>
    internal DurableTaskFactory(
        IDictionary<ActivityVersionKey, Func<IServiceProvider, ITaskActivity>> activities,
        IDictionary<OrchestratorVersionKey, Func<IServiceProvider, ITaskOrchestrator>> orchestrators,
        IDictionary<TaskName, Func<IServiceProvider, ITaskEntity>> entities)
    {
        this.activities = Check.NotNull(activities);
        this.orchestrators = Check.NotNull(orchestrators);
        this.entities = Check.NotNull(entities);

        // Snapshot the set of logical names that have at least one versioned registration. Used to gate the
        // unversioned-fallback path: when a logical name has any versioned registration, we refuse to fall
        // back to its unversioned registration for an unmatched versioned request — that would silently
        // route the call to a different implementation than the caller asked for.
        this.versionedOrchestratorNames = new HashSet<string>(
            this.orchestrators.Keys
                .Where(k => !string.IsNullOrWhiteSpace(k.Version))
                .Select(k => k.Name),
            StringComparer.OrdinalIgnoreCase);
        this.versionedActivityNames = new HashSet<string>(
            this.activities.Keys
                .Where(k => !string.IsNullOrWhiteSpace(k.Version))
                .Select(k => k.Name),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public bool TryCreateActivity(
        TaskName name,
        TaskVersion version,
        IServiceProvider serviceProvider,
        bool allowVersionFallback,
        [NotNullWhen(true)] out ITaskActivity? activity)
    {
        Check.NotNull(serviceProvider);
        ActivityVersionKey key = new(name, version);
        if (this.activities.TryGetValue(key, out Func<IServiceProvider, ITaskActivity>? factory))
        {
            activity = factory.Invoke(serviceProvider);
            return true;
        }

        if (allowVersionFallback
            && !string.IsNullOrWhiteSpace(version.Version)
            && !this.versionedActivityNames.Contains(name.Name)
            && this.activities.TryGetValue(new ActivityVersionKey(name, default(TaskVersion)), out factory))
        {
            activity = factory.Invoke(serviceProvider);
            return true;
        }

        activity = null;
        return false;
    }

    /// <inheritdoc/>
    public bool TryCreateActivity(
        TaskName name, IServiceProvider serviceProvider, [NotNullWhen(true)] out ITaskActivity? activity)
        => this.TryCreateActivity(name, default(TaskVersion), serviceProvider, allowVersionFallback: false, out activity);

    /// <inheritdoc/>
    public bool TryCreateOrchestrator(
        TaskName name,
        TaskVersion version,
        IServiceProvider serviceProvider,
        [NotNullWhen(true)] out ITaskOrchestrator? orchestrator)
    {
        Check.NotNull(serviceProvider);
        OrchestratorVersionKey key = new(name, version);
        if (this.orchestrators.TryGetValue(key, out Func<IServiceProvider, ITaskOrchestrator>? factory))
        {
            orchestrator = factory.Invoke(serviceProvider);
            return true;
        }

        // Unversioned registrations remain the compatibility fallback for a versioned request, but ONLY when
        // no versioned registration exists for the same logical name. If any versioned registration is present
        // (e.g., v1 and v2 are registered, request asks for v3), we refuse to silently route the call to a
        // catch-all registration the caller did not ask for.
        if (!string.IsNullOrWhiteSpace(version.Version)
            && !this.versionedOrchestratorNames.Contains(name.Name)
            && this.orchestrators.TryGetValue(new OrchestratorVersionKey(name, default(TaskVersion)), out factory))
        {
            orchestrator = factory.Invoke(serviceProvider);
            return true;
        }

        orchestrator = null;
        return false;
    }

    /// <inheritdoc/>
    public bool TryCreateOrchestrator(
        TaskName name, IServiceProvider serviceProvider, [NotNullWhen(true)] out ITaskOrchestrator? orchestrator)
        => this.TryCreateOrchestrator(name, default(TaskVersion), serviceProvider, out orchestrator);

    /// <inheritdoc/>
    public bool TryCreateEntity(
       TaskName name, IServiceProvider serviceProvider, [NotNullWhen(true)] out ITaskEntity? entity)
    {
        if (this.entities.TryGetValue(name, out Func<IServiceProvider, ITaskEntity>? factory))
        {
            entity = factory.Invoke(serviceProvider);
            return true;
        }

        entity = null;
        return false;
    }
}
