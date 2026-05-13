// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Creates orchestrator and activity instances by logical name and requested version.
/// Implemented by the default factory when the registry contains versioned registrations.
/// </summary>
internal interface IVersionedTaskFactory
{
    /// <summary>
    /// Tries to create an orchestrator that matches the provided logical name and version.
    /// </summary>
    /// <param name="name">The orchestrator name.</param>
    /// <param name="version">The orchestrator version.</param>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="orchestrator">The created orchestrator, if found.</param>
    /// <returns><c>true</c> if a matching orchestrator was created; otherwise <c>false</c>.</returns>
    bool TryCreateOrchestrator(
        TaskName name,
        TaskVersion version,
        IServiceProvider serviceProvider,
        [NotNullWhen(true)] out ITaskOrchestrator? orchestrator);

    /// <summary>
    /// Tries to create an activity that matches the provided logical name and version.
    /// </summary>
    /// <param name="name">The activity name.</param>
    /// <param name="version">The activity version.</param>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="activity">The created activity, if found.</param>
    /// <returns><c>true</c> if a matching activity was created; otherwise <c>false</c>.</returns>
    bool TryCreateActivity(
        TaskName name,
        TaskVersion version,
        IServiceProvider serviceProvider,
        [NotNullWhen(true)] out ITaskActivity? activity);
}
