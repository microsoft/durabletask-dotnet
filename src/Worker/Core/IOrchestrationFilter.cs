// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Defines a filter for validating orchestrations.
/// </summary>
public interface IOrchestrationFilter
{
    /// <summary>
    /// Validate the orchestration against the filter represented by this interface.
    /// </summary>
    /// <param name="info">The information on the orchestration to validate.</param>
    /// <param name="cancellationToken">The cancellation token for the request to timeout.</param>
    /// <returns><code>true</code> if the orchestration is valid <code>false</code> otherwise.</returns>
    Task<bool> IsOrchestrationValidAsync(OrchestrationInfo info, CancellationToken cancellationToken = default);
}

/// <summary>
/// Struct representation of orchestration information.
/// </summary>
public struct OrchestrationInfo
{
    /// <summary>
    /// Gets the name of the orchestration.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// Gets the tags associated with the orchestration.
    /// </summary>
    public Dictionary<string, string> Tags { get; init; }
}