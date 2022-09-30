// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Grpc.Core;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
