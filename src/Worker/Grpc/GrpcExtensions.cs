// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;
using Grpc.Core;

namespace Dapr.DurableTask;

/// <summary>
/// Extensions for gRPC.
/// </summary>
static class GrpcExtensions
{
    /// <summary>
    /// Reads all elements from an <see cref="IAsyncStreamReader{T}" />.
    /// </summary>
    /// <typeparam name="T">The type held by the stream.</typeparam>
    /// <param name="reader">The stream reader.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An <see cref="IAsyncEnumerable{T}" /> for consuming the stream.</returns>
    internal static async IAsyncEnumerable<T> ReadAllAsync<T>(
        this IAsyncStreamReader<T> reader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Check.NotNull(reader);
        while (await reader.MoveNext(cancellationToken).ConfigureAwait(false))
        {
            yield return reader.Current;
        }
    }
}
