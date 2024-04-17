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

    string? instanceIdStartsWith;

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
            if (value != null)
            {
                // prefix '@' if filter value provided and not already prefixed with '@'.
                string prefix = value.Length == 0 || value[0] != '@' ? $"@{value}" : value;

                // check if there is a name-key separator in the string
                int pos = prefix.IndexOf('@', 1);
                if (pos != -1)
                {
                    // selectively normalize only the part up until that separator
                    this.instanceIdStartsWith = prefix.Substring(0, pos).ToLowerInvariant() + prefix.Substring(pos);
                }
                else
                {
                    // normalize the entire prefix
                    this.instanceIdStartsWith = prefix.ToLowerInvariant();
                }
            }
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
    /// Gets a value indicating whether to include metadata about transient entities. Defaults to false.
    /// </summary>
    /// <remarks> Transient entities are entities that do not have an application-defined state, but for which the storage provider is
    /// tracking metadata for synchronization purposes.
    /// For example, a transient entity may be observed when the entity is in the process of being created or deleted, or
    /// when the entity has been locked by a critical section. By default, transient entities are not included in queries since they are
    /// considered to "not exist" from the perspective of the user application.
    /// </remarks>
    public bool IncludeTransient { get; init; }

    /// <summary>
    /// Gets the size of each page to return. If null, the page size is determined by the backend.
    /// </summary>
    public int? PageSize { get; init; }

    /// <summary>
    /// Gets the continuation token to resume a previous query.
    /// </summary>
    public string? ContinuationToken { get; init; }
}
