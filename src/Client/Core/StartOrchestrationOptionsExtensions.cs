// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Extension methods for <see cref="StartOrchestrationOptions"/> to provide type-safe deduplication status configuration.
/// </summary>
public static class StartOrchestrationOptionsExtensions
{
#pragma warning disable CS0618 // Type or member is obsolete - Cancelled is intentionally included for compatibility with the
                               // Durable Task Framework

    /// <summary>
    /// The list of orchestration statuses that can be deduplicated upon a creation request.
    /// If one of these statuses is included in the request via the <see cref="StartOrchestrationOptions.DedupeStatuses"/>
    /// field, and an orchestration with this status and same instance ID is found, the request will fail.
    /// </summary>
    public static readonly IReadOnlyList<OrchestrationRuntimeStatus> ValidDedupeStatuses =
    [
        OrchestrationRuntimeStatus.Completed,
        OrchestrationRuntimeStatus.Failed,
        OrchestrationRuntimeStatus.Terminated,
        OrchestrationRuntimeStatus.Canceled,
        OrchestrationRuntimeStatus.Pending,
        OrchestrationRuntimeStatus.Running,
        OrchestrationRuntimeStatus.Suspended,
    ];
#pragma warning restore CS0618 // Type or member is obsolete

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
}
