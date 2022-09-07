// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections;

namespace Microsoft.DurableTask;

/// <summary>
/// Represents a single page of results.
/// </summary>
public sealed class Page<T> : IReadOnlyList<T>
    where T : notnull
{
    // This code was adopted from Azure SDK Page.
    // https://github.com/Azure/azure-sdk-for-net/blob/e811f016a3655e4b29a23c71f84d59f34fe01233/sdk/core/Azure.Core/src/Page.cs

    readonly IReadOnlyList<T> values;

    /// <summary>
    /// Initializes a new instance of the <see cref="Page{T}" /> class.
    /// </summary>
    /// <param name="values">The values this holds.</param>
    /// <param name="continuationToken">The continuation token.</param>
    public Page(IReadOnlyList<T> values, string? continuationToken = null)
    {
        this.values = values ?? throw new ArgumentNullException(nameof(values));
        this.ContinuationToken = continuationToken;
    }

    /// <inheritdoc/>
    public T this[int index] => this.values[index];

    /// <summary>
    /// Gets the continuation token or <c>null</c> if there are no more items.
    /// </summary>
    public string? ContinuationToken { get; }

    /// <inheritdoc/>
    public int Count => this.values.Count;

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator() => this.values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
