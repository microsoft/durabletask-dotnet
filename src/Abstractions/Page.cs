// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Represents a single page of results.
/// </summary>
/// <typeparam name="T">The type of values held by the page.</typeparam>
public sealed class Page<T>
    where T : notnull
{
    // This code was adopted from Azure SDK Page.
    // https://github.com/Azure/azure-sdk-for-net/blob/e811f016a3655e4b29a23c71f84d59f34fe01233/sdk/core/Azure.Core/src/Page.cs

    /// <summary>
    /// Initializes a new instance of the <see cref="Page{T}" /> class.
    /// </summary>
    /// <param name="values">The values this holds.</param>
    /// <param name="continuationToken">The continuation token.</param>
    public Page(IReadOnlyList<T> values, string? continuationToken = null)
    {
        this.Values = Check.NotNull(values);
        this.ContinuationToken = continuationToken;
    }

    /// <summary>
    /// Gets the values contained in this page.
    /// </summary>
    public IReadOnlyList<T> Values { get; }

    /// <summary>
    /// Gets the continuation token or <c>null</c> if there are no more items.
    /// </summary>
    public string? ContinuationToken { get; }
}
