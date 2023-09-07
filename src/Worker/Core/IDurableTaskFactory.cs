// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// A factory for creating orchestrators and activities.
/// </summary>
public interface IDurableTaskFactory
{
    /// <summary>
    /// Tries to creates an activity given a name.
    /// </summary>
    /// <param name="name">The name of the activity.</param>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="activity">The activity or <c>null</c> if it does not exist.</param>
    /// <returns>True if activity was created, false otherwise.</returns>
    bool TryCreateActivity(
        TaskName name, IServiceProvider serviceProvider, [NotNullWhen(true)] out ITaskActivity? activity);

    /// <summary>
    /// Tries to creates an orchestrator given a name.
    /// </summary>
    /// <param name="name">The name of the orchestrator.</param>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="orchestrator">The orchestrator or <c>null</c> if it does not exist.</param>
    /// <returns>True if orchestrator was created, false otherwise.</returns>
    /// <remarks>
    /// While <paramref name="serviceProvider" /> is provided here, it is not required to be used to construct
    /// orchestrators. Individual implementations of this contract may use it in different ways. The default
    /// implementation does not use it.
    /// </remarks>
    bool TryCreateOrchestrator(
        TaskName name, IServiceProvider serviceProvider, [NotNullWhen(true)] out ITaskOrchestrator? orchestrator);
}

/// <summary>
/// A newer version of <see cref="IDurableTaskFactory"/> that adds support for entities.
/// </summary>
public interface IDurableTaskFactory2 : IDurableTaskFactory
{
    /// <summary>
    /// Tries to create an entity given a name.
    /// </summary>
    /// <param name="name">The name of the orchestrator.</param>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="entity">The entity or <c>null</c> if it does not exist.</param>
    /// <returns>True if entity was created, false otherwise.</returns>
    /// <remarks>
    /// While <paramref name="serviceProvider" /> is provided here, it is not required to be used to construct
    /// orchestrators. Individual implementations of this contract may use it in different ways. The default
    /// implementation does not use it.
    /// </remarks>
    bool TryCreateEntity(
        TaskName name, IServiceProvider serviceProvider, [NotNullWhen(true)] out ITaskEntity? entity);
}
