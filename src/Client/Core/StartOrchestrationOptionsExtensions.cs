// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Extension methods for <see cref="StartOrchestrationOptions"/> to provide type-safe deduplication status configuration.
/// </summary>
public static class StartOrchestrationOptionsExtensions
{
    /// <summary>
    /// Gets the terminal orchestration runtime statuses that are valid for deduplication.
    /// These are the statuses that can be used to prevent replacement of an existing orchestration instance.
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
