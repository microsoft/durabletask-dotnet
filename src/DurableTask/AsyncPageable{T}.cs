// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// A collection of values that may take multiple service requests to iterate over.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
public abstract class AsyncPageable<T> : IAsyncEnumerable<T>
    where T : notnull
{
    // This code was adapted from Azure SDK AsyncPageable.

    /// <summary>
    /// Enumerate the values a <see cref="Pageable.Page{T}" /> at a time.
    /// </summary>
    /// <param name="continuationToken">
    /// A continuation token indicating where to resume paging or null to begin paging from the
    /// beginning.
    /// </param>
    /// <param name="pageSizeHint">
    /// The number of items per <see cref="Pageable.Page{T}"/> that should be requested
    /// (from service operations that support it). It's not guaranteed that the value will be
    /// respected.
    /// </param>
    /// <returns>An async enumerable of pages.</returns>
    public abstract IAsyncEnumerable<Pageable.Page<T>> AsPages(
        string? continuationToken = default, int? pageSizeHint = default);

    /// <inheritdoc/>
    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        await foreach (Pageable.Page<T> page in this.AsPages()
            .ConfigureAwait(false).WithCancellation(cancellationToken))
        {
            foreach (T value in page)
            {
                yield return value;
            }
        }
    }
}
