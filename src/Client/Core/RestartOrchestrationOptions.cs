// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Options for restarting an orchestration from the beginning with a caller-selected instance ID.
/// </summary>
public sealed record RestartOrchestrationOptions
{
    /// <summary>
    /// Gets the required, nonblank instance ID of the restarted orchestration.
    /// </summary>
    public string NewInstanceId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional version override for the restarted orchestration.
    /// </summary>
    /// <remarks>
    /// A <c>null</c> value uses the backend's restart default, which need not be the original version.
    /// An empty string explicitly selects the unversioned orchestration.
    /// </remarks>
    public string? Version { get; init; }
}
