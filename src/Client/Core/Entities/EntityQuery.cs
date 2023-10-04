// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client.Entities;

/// <summary>
/// A query for fetching entities.
/// </summary>
public record EntityQuery
{
    string? instanceIdStartsWith;

    /// <summary>
    /// The default page size.
    /// </summary>
    public const int DefaultPageSize = 100;

    /// <summary>
    /// Gets the optional starts-with expression for the entity instance ID.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Entity IDs are expressed as <c>"@{name}@{key}"</c>. The starting "@" may be included or left out, the results will be
    /// the same. Including the separator "@" between name or key will have an affect.
    /// </para>
    /// <para>
    /// To query for an exact entity name, include the separator "@". e.g.: <c>"exactNameMatch@"</c>.
    /// </para>
    /// <para>
    /// To query for an entity name starts with, leave out the separator "@". e.g.: <c>"namePrefixMatch"</c>.
    /// </para>
    /// <para>
    /// To query for an entity name match <b>and</b> a key prefix, include name match, the separator "@", and finally
    /// the key prefix. e.g. <c>"exactNameMatch@keyPrefixMatch"</c>.
    /// </para>
    /// </remarks>
    public string? InstanceIdStartsWith
    {
        get => this.instanceIdStartsWith;
        init
        {
            // prefix '@' if filter value provided and not already prefixed with '@'.
            this.instanceIdStartsWith = value?.Length > 0 && value[0] != '@'
                ? $"@{value}" : value;
        }
    }

    /// <summary>
    /// Gets entity instances which were last modified after the provided time.
    /// </summary>
    public DateTimeOffset? LastModifiedFrom { get; init; }

    /// <summary>
    /// Gets entity instances which were last modified before the provided time.
    /// </summary>
    public DateTimeOffset? LastModifiedTo { get; init; }

    /// <summary>
    /// Gets a value indicating whether to include state in the query results or not. Defaults to true.
    /// </summary>
    public bool IncludeState { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether to include metadata about entities that have no user-defined state. Defaults to false.
    /// </summary>
    /// <remarks> Stateless entities occur when the storage provider is tracking metadata about an entity for synchronization purposes
    /// even though the entity does not "logically" exist, in the sense that it has no application-defined state.
    /// Stateless entities are usually transient. For example, they may be in the process of being created or deleted, or they may have been locked by a critical section.
    /// </remarks>
    public bool IncludeStateless { get; init; }

    /// <summary>
    /// Gets the size of each page to return. If null, the page size is determined by the backend.
    /// </summary>
    public int? PageSize { get; init; }

    /// <summary>
    /// Gets the continuation token to resume a previous query.
    /// </summary>
    public string? ContinuationToken { get; init; }
}
