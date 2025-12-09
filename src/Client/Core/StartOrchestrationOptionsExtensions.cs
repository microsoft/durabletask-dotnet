// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Extension methods for <see cref="StartOrchestrationOptions"/> to provide type-safe deduplication status configuration.
/// </summary>
public static class StartOrchestrationOptionsExtensions
{
    /// <summary>
    /// Gets the terminal orchestration runtime statuses commonly used for deduplication.
    /// These are typically the statuses used to prevent replacement of an existing orchestration instance.
    /// Note: Any <see cref="OrchestrationRuntimeStatus"/> value can be used for deduplication;
    /// this collection is provided for convenience and reference only.
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
}
