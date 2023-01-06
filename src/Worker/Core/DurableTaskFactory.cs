// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using Microsoft.DurableTask.Worker.DependencyInjection;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// A factory for creating orchestrators and activities.
/// </summary>
sealed class DurableTaskFactory : IDurableTaskFactory
{
    readonly IDictionary<TaskName, Func<IServiceProvider, ITaskActivity>> activities;
    readonly IDictionary<TaskName, Func<IServiceProvider, ITaskOrchestrator>> orchestrators;

    /// <summary>
    /// Initializes a new instance of the <see cref="DurableTaskFactory" /> class.
    /// </summary>
    /// <param name="activities">The activity factories.</param>
    /// <param name="orchestrators">The orchestrator factories.</param>
    internal DurableTaskFactory(
        IDictionary<TaskName, Func<IServiceProvider, ITaskActivity>> activities,
        IDictionary<TaskName, Func<IServiceProvider, ITaskOrchestrator>> orchestrators)
    {
        this.activities = Check.NotNull(activities);
        this.orchestrators = Check.NotNull(orchestrators);
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
            orchestrator = factory.Invoke(new OrchestrationServiceProvider(serviceProvider));
            return true;
        }

        orchestrator = null;
        return false;
    }
}
