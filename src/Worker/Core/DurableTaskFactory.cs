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
    readonly bool useOrchestratorUnversionedFallback;
    readonly bool useActivityUnversionedFallback;
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
        DurableTaskWorkerOptions.UnversionedFallbackMode orchestratorUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.Never,
        DurableTaskWorkerOptions.UnversionedFallbackMode activityUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.Never,
        ILoggerFactory? loggerFactory = null)
    {
        this.activities = Check.NotNull(activities);
        this.orchestrators = Check.NotNull(orchestrators);
        this.entities = Check.NotNull(entities);
        this.useOrchestratorUnversionedFallback =
            orchestratorUnversionedFallback == DurableTaskWorkerOptions.UnversionedFallbackMode.CatchAll;
        this.useActivityUnversionedFallback =
            activityUnversionedFallback == DurableTaskWorkerOptions.UnversionedFallbackMode.CatchAll;
        this.logger = loggerFactory is not null ? Logs.CreateWorkerLogger(loggerFactory) : null;

        // Snapshot the set of logical names that have at least one versioned registration. By default, this gates
        // unversioned fallback so a mixed versioned/unversioned name remains a closed set. Workers can opt in to
        // allowing the unversioned registration to handle unmatched versions, independently for orchestrators
        // and activities.
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

        // Unversioned registrations remain the compatibility fallback for a versioned request when no versioned
        // registration exists for the same logical name. Workers can also opt in to treating the unversioned
        // registration as a catch-all for unmatched versions.
        if (!string.IsNullOrWhiteSpace(version.Version)
            && (this.useActivityUnversionedFallback || !this.versionedActivityNames.Contains(name.Name))
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

        // Unversioned registrations remain the compatibility fallback for a versioned request when no versioned
        // registration exists for the same logical name. Workers can also opt in to treating the unversioned
        // registration as a catch-all for unmatched versions.
        if (!string.IsNullOrWhiteSpace(version.Version)
            && (this.useOrchestratorUnversionedFallback || !this.versionedOrchestratorNames.Contains(name.Name))
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
}
