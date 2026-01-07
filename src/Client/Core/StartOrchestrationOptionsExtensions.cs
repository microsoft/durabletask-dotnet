// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Extension methods for <see cref="StartOrchestrationOptions"/> to provide type-safe deduplication status configuration.
/// </summary>
public static class StartOrchestrationOptionsExtensions
{
    /// <summary>
    /// Gets the valid terminal orchestration statuses that can be used for deduplication and ID reuse policies.
    /// </summary>
    public static readonly OrchestrationRuntimeStatus[] ValidDedupeStatuses = new[]
    {
        OrchestrationRuntimeStatus.Completed,
        OrchestrationRuntimeStatus.Failed,
        OrchestrationRuntimeStatus.Terminated,
        OrchestrationRuntimeStatus.Canceled,
    };

    /// <summary>
    /// Creates a new <see cref="StartOrchestrationOptions"/> with the specified deduplication statuses.
    /// </summary>
    /// <param name="options">The base options to extend.</param>
    /// <param name="dedupeStatuses">The orchestration runtime statuses that should be considered for deduplication.</param>
    /// <returns>A new <see cref="StartOrchestrationOptions"/> instance with the deduplication statuses set.</returns>
    public static StartOrchestrationOptions WithDedupeStatuses(
        this StartOrchestrationOptions options,
        params OrchestrationRuntimeStatus[] dedupeStatuses)
    {
        return options with
        {
            DedupeStatuses = dedupeStatuses.Select(s => s.ToString()).ToList(),
        };
    }

    /// <summary>
    /// Creates a new <see cref="StartOrchestrationOptions"/> with the specified orchestration ID reuse policy.
    /// </summary>
    /// <param name="options">The base options to extend.</param>
    /// <param name="policy">The orchestration ID reuse policy.</param>
    /// <returns>A new <see cref="StartOrchestrationOptions"/> instance with the ID reuse policy set.</returns>
    public static StartOrchestrationOptions WithIdReusePolicy(
        this StartOrchestrationOptions options,
        OrchestrationIdReusePolicy policy)
    {
        Check.NotNull(policy);
        return options with
        {
            IdReusePolicy = policy,
        };
    }
}
