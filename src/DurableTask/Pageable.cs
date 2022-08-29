// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections;
using System.Runtime.CompilerServices;

namespace Microsoft.DurableTask;

/// <summary>
/// Pageable helpers.
/// </summary>
public static class Pageable
{
    // This code was adapted from Azure SDK AsyncPageable.
    // TODO: Add Pageable<T> (non-async) when/if it becomes relevant.

    /// <summary>
    /// Creates an async pageable from a callback function <paramref name="pageFunc" />.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="pageFunc">The callback to fetch additional pages.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    public static AsyncPageable<T> Create<T>(Func<string?, CancellationToken, Task<Page<T>>> pageFunc)
        where T : notnull
    {
        if (pageFunc is null)
        {
            throw new ArgumentNullException(nameof(pageFunc));
        }

        return Create((continuation, size, cancellation) => pageFunc(continuation, cancellation));
    }

    /// <summary>
    /// Creates an async pageable from a callback function <paramref name="pageFunc" />.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="pageFunc">The callback to fetch additional pages.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    public static AsyncPageable<T> Create<T>(Func<string?, int?, CancellationToken, Task<Page<T>>> pageFunc)
        where T : notnull
    {
        if (pageFunc is null)
        {
            throw new ArgumentNullException(nameof(pageFunc));
        }

        return new FuncAsyncPageable<T>(pageFunc);
    }

    /// <summary>
    /// Represents a single page of results.
    /// </summary>
    public sealed class Page<T> : IReadOnlyList<T>
        where T : notnull
    {
        readonly IReadOnlyList<T> values;

        /// <summary>
        /// Initializes a new instance of the <see cref="Page{T}" /> class.
        /// </summary>
        /// <param name="values">The values this holds.</param>
        /// <param name="continuationToken">The continuation token.</param>
        public Page(IReadOnlyList<T> values, string? continuationToken = null)
        {
            this.values = values;
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

    class FuncAsyncPageable<T> : AsyncPageable<T>
        where T : notnull
    {
        readonly Func<string?, int?, CancellationToken, Task<Page<T>>> pageFunc;

        public FuncAsyncPageable(Func<string?, int?, CancellationToken, Task<Page<T>>> pageFunc)
        {
            this.pageFunc = pageFunc;
        }

        public override IAsyncEnumerable<Page<T>> AsPages(
            string? continuationToken = default, int? pageSizeHint = default)
            => this.AsPagesCore(continuationToken, pageSizeHint);

        async IAsyncEnumerable<Page<T>> AsPagesCore(
            string? continuationToken = default,
            int? pageSizeHint = default,
            [EnumeratorCancellation] CancellationToken cancellation = default)
        {
            do
            {
                Page<T> page = await this.pageFunc(continuationToken, pageSizeHint, cancellation)
                    .ConfigureAwait(false);
                yield return page;
                continuationToken = page.ContinuationToken;
            }
            while (continuationToken is not null);
        }
    }
}
