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
    }

    /// <inheritdoc/>
    public bool TryCreateActivity(
        TaskName name,
        TaskVersion version,
        IServiceProvider serviceProvider,
        [NotNullWhen(true)] out ITaskActivity? activity)
    {
        Check.NotNull(serviceProvider);
        ActivityVersionKey key = new(name, version);
        if (this.activities.TryGetValue(key, out Func<IServiceProvider, ITaskActivity>? factory))
        {
            activity = factory.Invoke(serviceProvider);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(version.Version)
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
        => this.TryCreateActivity(name, default(TaskVersion), serviceProvider, out activity);

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

        // Unversioned registrations remain the compatibility fallback when a caller requests a version that has
        // no exact match for the logical orchestrator name.
        if (!string.IsNullOrWhiteSpace(version.Version)
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
