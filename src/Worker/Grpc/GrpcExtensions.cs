// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;
using Grpc.Core;

namespace Microsoft.DurableTask;

static class GrpcExtensions
{
    internal static async IAsyncEnumerable<T> ReadAllAsync<T>(
        this IAsyncStreamReader<T> reader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (reader == null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        while (await reader.MoveNext(cancellationToken).ConfigureAwait(false))
        {
            yield return reader.Current;
        }
    }
}
