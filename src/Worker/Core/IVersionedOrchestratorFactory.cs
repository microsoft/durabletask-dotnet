// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Creates orchestrator instances by exact logical name and version.
/// </summary>
internal interface IVersionedOrchestratorFactory
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
}
