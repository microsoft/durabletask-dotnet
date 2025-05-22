// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using Dapr.DurableTask.Entities;

namespace Dapr.DurableTask.Worker;

/// <summary>
/// A factory for creating orchestrators and activities.
/// </summary>
sealed class DurableTaskFactory : IDurableTaskFactory2
{
    readonly IDictionary<TaskName, Func<IServiceProvider, ITaskActivity>> activities;
    readonly IDictionary<TaskName, Func<IServiceProvider, ITaskOrchestrator>> orchestrators;
    readonly IDictionary<TaskName, Func<IServiceProvider, ITaskEntity>> entities;

    /// <summary>
    /// Initializes a new instance of the <see cref="DurableTaskFactory" /> class.
    /// </summary>
    /// <param name="activities">The activity factories.</param>
    /// <param name="orchestrators">The orchestrator factories.</param>
    /// <param name="entities">The entity factories.</param>
    internal DurableTaskFactory(
        IDictionary<TaskName, Func<IServiceProvider, ITaskActivity>> activities,
        IDictionary<TaskName, Func<IServiceProvider, ITaskOrchestrator>> orchestrators,
        IDictionary<TaskName, Func<IServiceProvider, ITaskEntity>> entities)
    {
        this.activities = Check.NotNull(activities);
        this.orchestrators = Check.NotNull(orchestrators);
        this.entities = Check.NotNull(entities);
    }

    /// <inheritdoc/>
    public bool TryCreateActivity(
        TaskName name, IServiceProvider serviceProvider, [NotNullWhen(true)] out ITaskActivity? activity)
    {
        Check.NotNull(serviceProvider);
        if (this.activities.TryGetValue(name, out Func<IServiceProvider, ITaskActivity>? factory))
        {
            activity = factory.Invoke(serviceProvider);
            return true;
        }

        activity = null;
        return false;
    }

    /// <inheritdoc/>
    public bool TryCreateOrchestrator(
        TaskName name, IServiceProvider serviceProvider, [NotNullWhen(true)] out ITaskOrchestrator? orchestrator)
    {
        if (this.orchestrators.TryGetValue(name, out Func<IServiceProvider, ITaskOrchestrator>? factory))
        {
            orchestrator = factory.Invoke(serviceProvider);
            return true;
        }

        orchestrator = null;
        return false;
    }

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
