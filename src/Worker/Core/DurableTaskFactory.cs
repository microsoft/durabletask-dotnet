// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// A factory for creating orchestrators and activities.
/// </summary>
sealed class DurableTaskFactory : IDurableTaskFactory2, IVersionedTaskFactory
{
    readonly IDictionary<TaskVersionKey, Func<IServiceProvider, ITaskActivity>> activities;
    readonly IDictionary<TaskVersionKey, Func<IServiceProvider, ITaskOrchestrator>> orchestrators;
    readonly IDictionary<TaskName, Func<IServiceProvider, ITaskEntity>> entities;
    readonly HashSet<string> versionedOrchestratorNames;
    readonly HashSet<string> versionedActivityNames;
    readonly DurableTaskWorkerOptions.UnversionedFallbackMode orchestratorFallbackMode;
    readonly DurableTaskWorkerOptions.UnversionedFallbackMode activityFallbackMode;
    readonly ILogger? logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DurableTaskFactory" /> class.
    /// </summary>
    /// <param name="activities">The activity factories.</param>
    /// <param name="orchestrators">The orchestrator factories.</param>
    /// <param name="entities">The entity factories.</param>
    /// <param name="orchestratorUnversionedFallback">The unversioned fallback mode for orchestrators.</param>
    /// <param name="activityUnversionedFallback">The unversioned fallback mode for activities.</param>
    /// <param name="loggerFactory">Optional logger factory used to emit per-dispatch fallback diagnostics.</param>
    internal DurableTaskFactory(
        IDictionary<TaskVersionKey, Func<IServiceProvider, ITaskActivity>> activities,
        IDictionary<TaskVersionKey, Func<IServiceProvider, ITaskOrchestrator>> orchestrators,
        IDictionary<TaskName, Func<IServiceProvider, ITaskEntity>> entities,
        DurableTaskWorkerOptions.UnversionedFallbackMode orchestratorUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.Implicit,
        DurableTaskWorkerOptions.UnversionedFallbackMode activityUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.Implicit,
        ILoggerFactory? loggerFactory = null)
    {
        this.activities = Check.NotNull(activities);
        this.orchestrators = Check.NotNull(orchestrators);
        this.entities = Check.NotNull(entities);
        this.orchestratorFallbackMode = orchestratorUnversionedFallback;
        this.activityFallbackMode = activityUnversionedFallback;
        this.logger = loggerFactory is not null ? Logs.CreateWorkerLogger(loggerFactory) : null;

        // Snapshot the set of logical names that have at least one versioned registration. Used by the
        // Implicit fallback mode to recognize "unversioned-only" names, where a versioned request is allowed
        // to resolve through the unversioned registration. CatchAll widens this for mixed names; StrictExactOnly
        // disables fallback entirely.
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
        [NotNullWhen(true)] out ITaskActivity? activity)
    {
        Check.NotNull(serviceProvider);
        TaskVersionKey key = new(name, version);
        if (this.activities.TryGetValue(key, out Func<IServiceProvider, ITaskActivity>? factory))
        {
            activity = factory.Invoke(serviceProvider);
            return true;
        }

        // Resolve a versioned request through the unversioned registration when the mode allows it.
        // See UnversionedFallbackMode for the dispatch matrix.
        if (!string.IsNullOrWhiteSpace(version.Version)
            && ShouldUseUnversionedFallback(this.activityFallbackMode, this.versionedActivityNames, name.Name)
            && this.activities.TryGetValue(new TaskVersionKey(name, default(TaskVersion)), out factory))
        {
            this.logger?.ActivityDispatchedToUnversionedFallback(name.Name, version.Version);
            activity = factory.Invoke(serviceProvider);
            return true;
        }

        activity = null;
        return false;
    }

    /// <inheritdoc/>
    public bool TryCreateActivity(
        TaskName name, IServiceProvider serviceProvider, [NotNullWhen(true)] out ITaskActivity? activity)
        => this.TryCreateActivity(name, default(TaskVersion), serviceProvider, out activity);

    /// <inheritdoc/>
    public bool TryCreateOrchestrator(
        TaskName name,
        TaskVersion version,
        IServiceProvider serviceProvider,
        [NotNullWhen(true)] out ITaskOrchestrator? orchestrator)
    {
        Check.NotNull(serviceProvider);
        TaskVersionKey key = new(name, version);
        if (this.orchestrators.TryGetValue(key, out Func<IServiceProvider, ITaskOrchestrator>? factory))
        {
            orchestrator = factory.Invoke(serviceProvider);
            return true;
        }

        // Resolve a versioned request through the unversioned registration when the mode allows it.
        // See UnversionedFallbackMode for the dispatch matrix.
        if (!string.IsNullOrWhiteSpace(version.Version)
            && ShouldUseUnversionedFallback(this.orchestratorFallbackMode, this.versionedOrchestratorNames, name.Name)
            && this.orchestrators.TryGetValue(new TaskVersionKey(name, default(TaskVersion)), out factory))
        {
            this.logger?.OrchestratorDispatchedToUnversionedFallback(name.Name, version.Version);
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

    static bool ShouldUseUnversionedFallback(
        DurableTaskWorkerOptions.UnversionedFallbackMode mode,
        HashSet<string> versionedNames,
        string requestedName)
    {
        return mode switch
        {
            DurableTaskWorkerOptions.UnversionedFallbackMode.StrictExactOnly => false,
            DurableTaskWorkerOptions.UnversionedFallbackMode.CatchAll => true,
            DurableTaskWorkerOptions.UnversionedFallbackMode.Implicit => !versionedNames.Contains(requestedName),
            _ => !versionedNames.Contains(requestedName),
        };
    }
}
