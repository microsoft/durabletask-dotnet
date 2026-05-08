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
    /// When set, the purge operation stops deleting additional instances after this duration elapses
    /// and returns a partial result. Callers can check <see cref="PurgeResult.IsComplete"/> and
    /// re-invoke the purge to continue where it left off.
    /// The value of <see cref="PurgeResult.IsComplete"/> depends on the backend implementation:
    /// it may be <c>false</c> if the purge timed out, <c>true</c> if all instances were purged,
    /// or <c>null</c> if the backend does not support reporting completion status.
    /// Not all backends support this property; those that do not will ignore it.
    /// </summary>
    public TimeSpan? Timeout { get; init; }
}
