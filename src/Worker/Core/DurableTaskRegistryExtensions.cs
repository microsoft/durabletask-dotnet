// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Extensions for <see cref="DurableTaskRegistry" />.
/// </summary>
static class DurableTaskRegistryExtensions
{
    /// <summary>
    /// Builds a <see cref="DurableTaskRegistry" /> into a <see cref="IDurableTaskFactory" />.
    /// </summary>
    /// <param name="registry">The registry to build.</param>
    /// <returns>The built factory.</returns>
    public static IDurableTaskFactory BuildFactory(this DurableTaskRegistry registry)
    {
        Check.NotNull(registry);
        return new DurableTaskFactory(registry.Activities, registry.Orchestrators);
    }
}
