// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// A filter for querying orchestration instances.
/// </summary>
/// <param name="CreatedFrom">Creation date of instances to query from.</param>
/// <param name="CreatedTo">Creation date of instances to query to.</param>
/// <param name="Statuses">Runtime statuses of instances to query.</param>
/// <param name="TaskHubNames">Names of task hubs to query across.</param>
/// <param name="InstanceIdPrefix">Prefix of instance IDs to include.</param>
/// <param name="PageSize">Max item count to include per page.</param>
/// <param name="FetchInputsAndOutputs">Whether to include instance inputs or outputs in the query results.</param>
/// <param name="ContinuationToken">The continuation token to continue a paged query.</param>
public record OrchestrationQuery(
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null,
    IEnumerable<OrchestrationRuntimeStatus>? Statuses = null,
    IEnumerable<string>? TaskHubNames = null,
    string? InstanceIdPrefix = null,
    int PageSize = OrchestrationQuery.DefaultPageSize,
    bool FetchInputsAndOutputs = false,
    string? ContinuationToken = null)
{
    /// <summary>
    /// The default page size when not supplied.
    /// </summary>
    public const int DefaultPageSize = 100;
}
