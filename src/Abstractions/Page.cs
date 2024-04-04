// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Represents a single page of results.
/// </summary>
/// <typeparam name="T">The type of values held by the page.</typeparam>
/// <param name="values">The values this holds.</param>
/// <param name="continuationToken">The continuation token.</param>
public sealed class Page<T>(IReadOnlyList<T> values, string? continuationToken = null)
    where T : notnull
{
    /// <summary>
    /// Gets the values contained in this page.
    /// </summary>
    public IReadOnlyList<T> Values { get; } = Check.NotNull(values);

    /// <summary>
    /// Gets the continuation token or <c>null</c> if there are no more items.
    /// </summary>
    public string? ContinuationToken { get; } = continuationToken;
}
