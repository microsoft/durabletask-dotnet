// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System.Diagnostics.CodeAnalysis;

namespace Dapr.DurableTask;

/// <summary>
/// Helpers for <see cref="InvalidOperationException" /> assertions.
/// </summary>
static class Verify
{
    /// <summary>
    /// Verify some argument is not null, throwing <see cref="InvalidOperationException" /> if it is.
    /// </summary>
    /// <typeparam name="T">The type of the argument.</typeparam>
    /// <param name="argument">The argument to verify.</param>
    /// <param name="message">The optional exception message.</param>
    /// <returns>The provided <paramref name="argument" />.</returns>
    [return: NotNullIfNotNull("argument")]
    public static T NotNull<T>([NotNull] T? argument, string? message = default)
        where T : class
    {
        if (argument is null)
        {
            throw message is null
                ? new InvalidOperationException()
                : new InvalidOperationException(message);
        }

        return argument;
    }
}
