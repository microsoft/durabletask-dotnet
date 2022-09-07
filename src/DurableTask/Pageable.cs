// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;

namespace Microsoft.DurableTask;

/// <summary>
/// Pageable helpers.
/// </summary>
static class Pageable
{
    // This code was adapted from Azure SDK PageResponseEnumerator.
    // https://github.com/Azure/azure-sdk-for-net/blob/e811f016a3655e4b29a23c71f84d59f34fe01233/sdk/core/Azure.Core/src/Shared/PageResponseEnumerator.cs
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
