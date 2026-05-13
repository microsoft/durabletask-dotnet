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
        return new DurableTaskFactory(registry.Activities, registry.Orchestrators, registry.Entities);
    }

    /// <summary>
    /// Returns a value indicating whether any orchestrator or activity in the registry has been
    /// registered with an explicit (non-empty) <see cref="DurableTaskVersionAttribute"/>-style version.
    /// </summary>
    /// <param name="registry">The registry to inspect.</param>
    /// <returns><c>true</c> if any registration carries a non-empty version; otherwise, <c>false</c>.</returns>
    internal static bool HasAnyVersionedRegistration(this DurableTaskRegistry registry)
    {
        Check.NotNull(registry);
        foreach (TaskVersionKey key in registry.Orchestrators.Keys)
        {
            if (!string.IsNullOrWhiteSpace(key.Version))
            {
                return true;
            }
        }

        foreach (TaskVersionKey key in registry.Activities.Keys)
        {
            if (!string.IsNullOrWhiteSpace(key.Version))
            {
                return true;
            }
        }

        return false;
    }
}
