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
        => registry.BuildFactory(null);

    /// <summary>
    /// Builds a <see cref="DurableTaskRegistry" /> into a <see cref="IDurableTaskFactory" />.
    /// </summary>
    /// <param name="registry">The registry to build.</param>
    /// <param name="workerOptions">The worker options to use when building the factory.</param>
    /// <returns>The built factory.</returns>
    public static IDurableTaskFactory BuildFactory(this DurableTaskRegistry registry, DurableTaskWorkerOptions? workerOptions)
    {
        Check.NotNull(registry);
        DurableTaskWorkerOptions.UnversionedFallbackMode unversionedFallback =
            workerOptions?.Versioning?.UnversionedFallback ?? DurableTaskWorkerOptions.UnversionedFallbackMode.Never;
        return new DurableTaskFactory(
            registry.ActivitiesByVersion,
            registry.OrchestratorsByVersion,
            registry.Entities,
            unversionedFallback);
    }
}
