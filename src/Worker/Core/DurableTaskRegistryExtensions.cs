// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

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
        => registry.BuildFactory(workerOptions: null, loggerFactory: null);

    /// <summary>
    /// Builds a <see cref="DurableTaskRegistry" /> into a <see cref="IDurableTaskFactory" />.
    /// </summary>
    /// <param name="registry">The registry to build.</param>
    /// <param name="workerOptions">The worker options to use when building the factory.</param>
    /// <param name="loggerFactory">Optional logger factory used to emit per-dispatch fallback diagnostics.</param>
    /// <returns>The built factory.</returns>
    public static IDurableTaskFactory BuildFactory(
        this DurableTaskRegistry registry,
        DurableTaskWorkerOptions? workerOptions,
        ILoggerFactory? loggerFactory = null)
    {
        Check.NotNull(registry);
        DurableTaskWorkerOptions.UnversionedFallbackMode orchestratorFallback =
            workerOptions?.Versioning?.OrchestratorUnversionedFallback
                ?? DurableTaskWorkerOptions.UnversionedFallbackMode.Never;
        DurableTaskWorkerOptions.UnversionedFallbackMode activityFallback =
            workerOptions?.Versioning?.ActivityUnversionedFallback
                ?? DurableTaskWorkerOptions.UnversionedFallbackMode.Never;
        return new DurableTaskFactory(
            registry.ActivitiesByVersion,
            registry.OrchestratorsByVersion,
            registry.Entities,
            orchestratorFallback,
            activityFallback,
            loggerFactory);
    }
}
