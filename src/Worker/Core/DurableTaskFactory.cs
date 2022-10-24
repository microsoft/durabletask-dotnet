// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// A factory for creating orchestrators and activities.
/// </summary>
public sealed class DurableTaskFactory
{
    readonly IReadOnlyDictionary<TaskName, Func<IServiceProvider, ITaskActivity>> activities;
    readonly IReadOnlyDictionary<TaskName, Func<ITaskOrchestrator>> orchestrators;

    /// <summary>
    /// Initializes a new instance of the <see cref="DurableTaskFactory" /> class.
    /// </summary>
    /// <param name="activities">The activity factories.</param>
    /// <param name="orchestrators">The orchestrator factories.</param>
    internal DurableTaskFactory(
        IReadOnlyDictionary<TaskName, Func<IServiceProvider, ITaskActivity>> activities,
        IReadOnlyDictionary<TaskName, Func<ITaskOrchestrator>> orchestrators)
    {
        this.activities = Check.NotNull(activities);
        this.orchestrators = Check.NotNull(orchestrators);
    }

    /// <summary>
    /// Tries to creates an activity given a name.
    /// </summary>
    /// <param name="name">The name of the activity.</param>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="activity">The activity or <c>null</c> if it does not exist.</param>
    /// <returns>True if activity was created, false otherwise.</returns>
    public bool TryCreateActivity(
        TaskName name,
        IServiceProvider serviceProvider,
        [NotNullWhen(true)] out ITaskActivity? activity)
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

    /// <summary>
    /// Creates an orchestrator given a name.
    /// </summary>
    /// <param name="name">The name of the orchestrator.</param>
    /// <param name="orchestrator">The orchestrator or <c>null</c> if it does not exist.</param>
    /// <returns>The task orchestrator.</returns>
    public bool TryCreateOrchestrator(TaskName name, [NotNullWhen(true)] out ITaskOrchestrator? orchestrator)
    {
        if (this.orchestrators.TryGetValue(name, out Func<ITaskOrchestrator>? factory))
        {
            orchestrator = factory.Invoke();
            return true;
        }

        orchestrator = null;
        return false;
    }
}
