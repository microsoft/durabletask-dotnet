// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client.Entities;

/// <summary>
/// A query for fetching entities.
/// </summary>
public record EntityQuery
{
    /// <summary>
    /// The default page size.
    /// </summary>
    public const int DefaultPageSize = 100;

    /// <summary>
    /// Gets the optional name of the entity to query for.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets entity instances which was last modified after the provided time.
    /// </summary>
    public DateTimeOffset? LastModifiedFrom { get; init; }

    /// <summary>
    /// Gets entity instances which was last modified before the provided time.
    /// </summary>
    public DateTimeOffset? LastModifiedTo { get; init; }

    /// <summary>
    /// Gets a value indicating whether to include state in the query results or not.
    /// </summary>
    public bool IncludeState { get; init; }

    /// <summary>
    /// Gets the size of each page to return.
    /// </summary>
    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>
    /// Gets the continuation token to resume a previous query.
    /// </summary>
    public string? ContinuationToken { get; init; }
}
