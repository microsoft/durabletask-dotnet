// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Creates orchestrator and activity instances by logical name and requested version.
/// Implemented by the default <see cref="DurableTaskFactory"/>; the gRPC processor uses this
/// version-aware overload when the factory exposes it and falls back to the name-only
/// <see cref="IDurableTaskFactory"/> overload otherwise (for custom factories that only implement
/// the public interface).
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
