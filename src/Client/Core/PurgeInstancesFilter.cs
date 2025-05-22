// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Dapr.DurableTask.Client;

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
}
