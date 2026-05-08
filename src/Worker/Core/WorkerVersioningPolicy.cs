// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Worker startup checks that all <see cref="DurableTaskWorker"/> subclasses should perform before they
/// begin processing work items. Centralizing the checks keeps every transport (gRPC, in-proc, future
/// transports) on the same set of guarantees.
/// </summary>
internal static class WorkerVersioningPolicy
{
    /// <summary>
    /// Throws when worker-level <see cref="DurableTaskWorkerOptions.VersioningOptions"/> is configured
    /// alongside per-task <c>[DurableTaskVersion]</c> registrations. The two features both consume the
    /// orchestration instance version field; combining them silently masks per-task routing because the
    /// worker-level filter rejects versioned work items before per-task dispatch can run.
    /// </summary>
    /// <param name="workerName">The worker name (for the diagnostic message).</param>
    /// <param name="workerOptions">The worker options.</param>
    /// <param name="registry">The registry, or <c>null</c> if the worker did not opt into the check.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when both worker-level and per-task versioning are configured.
    /// </exception>
    public static void EnsureNotCombined(
        string workerName,
        DurableTaskWorkerOptions workerOptions,
        DurableTaskRegistry? registry)
    {
        Check.NotNull(workerOptions);
        if (registry is null)
        {
            return;
        }

        if (workerOptions.Versioning is not DurableTaskWorkerOptions.VersioningOptions versioning
            || versioning.MatchStrategy == DurableTaskWorkerOptions.VersionMatchStrategy.None)
        {
            return;
        }

        if (!registry.HasAnyVersionedRegistration())
        {
            return;
        }

        throw new InvalidOperationException(
            $"Worker '{workerName}' has both worker-level versioning (UseVersioning with MatchStrategy = '{versioning.MatchStrategy}') "
            + $"and per-task [DurableTaskVersion] registrations configured. These features are not designed to be combined: "
            + $"worker-level version checks run before per-task dispatch and will reject orchestrations whose instance version "
            + $"does not match the worker version, silently masking per-task routing. Pick one. "
            + $"See https://aka.ms/durabletask-versioning for guidance.");
    }
}
