// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.Entities;

namespace Microsoft.DurableTask.Worker.OrchestrationServiceShim.Core;

/// <summary>
/// A shim activity manager which allows for creating the actual activity in the middleware.
/// </summary>
sealed class ShimEntityManager : INameVersionObjectManager<TaskEntity>
{
    /// <inheritdoc/>
    public void Add(ObjectCreator<TaskEntity> creator) => throw new NotSupportedException();

    /// <inheritdoc/>
    public TaskEntity? GetObject(string name, string? version) => null;
}
