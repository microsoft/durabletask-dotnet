// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;

namespace Microsoft.DurableTask.Worker.OrchestrationServiceShim.Core;

/// <summary>
/// A shim activity manager which allows for creating the actual activity in the middleware.
/// </summary>
sealed class ShimOrchestrationManager : INameVersionObjectManager<TaskOrchestration>
{
    /// <inheritdoc/>
    public void Add(ObjectCreator<TaskOrchestration> creator) => throw new NotSupportedException();

    /// <inheritdoc/>
    public TaskOrchestration? GetObject(string name, string? version) => null;
}
