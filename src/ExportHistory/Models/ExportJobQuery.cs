// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Represents query parameters for filtering export history jobs.
/// </summary>
public record ExportJobQuery
{
    /// <summary>
    /// The default page size when not supplied.
    /// </summary>
    public const int DefaultPageSize = 100;

    /// <summary>
    /// Gets the filter for the export history job status.
    /// </summary>
    public ExportJobStatus? Status { get; init; }

    /// <summary>
    /// Gets the prefix to filter export history job IDs.
    /// </summary>
    public string? JobIdPrefix { get; init; }

    /// <summary>
    /// Gets the filter for export history jobs created after this time.
    /// </summary>
    public DateTimeOffset? CreatedFrom { get; init; }

    /// <summary>
    /// Gets the filter for export history jobs created before this time.
    /// </summary>
    public DateTimeOffset? CreatedTo { get; init; }

    /// <summary>
    /// Gets the maximum number of export history jobs to return per page.
    /// </summary>
    public int? PageSize { get; init; }

    /// <summary>
    /// Gets the continuation token for retrieving the next page of results.
    /// </summary>
    public string? ContinuationToken { get; init; }
}
