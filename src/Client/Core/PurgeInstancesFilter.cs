// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Query for purging orchestration instances.
/// </summary>
/// <param name="CreatedFrom">Date created from.</param>
/// <param name="CreatedTo">Date created to.</param>
/// <param name="Statuses">The statuses.</param>
public record PurgeInstancesFilter(
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null,
    IEnumerable<OrchestrationRuntimeStatus>? Statuses = null)
{
    /// <summary>
    /// Gets or sets the maximum amount of time to spend purging instances in a single call.
    /// If <c>null</c> (default), all matching instances are purged with no time limit.
    /// When set, the purge stops accepting new instances after this duration elapses
    /// and returns with <see cref="PurgeResult.IsComplete"/> set to <c>false</c>.
    /// Already-started instance deletions will complete before the method returns.
    /// </summary>
    public TimeSpan? Timeout { get; init; }
}
