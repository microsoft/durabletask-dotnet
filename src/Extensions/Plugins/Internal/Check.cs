// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Minimal argument validation helpers for the Plugins extension package.
/// </summary>
static class Check
{
    public static T NotNull<T>(T? argument, string? name = null)
        where T : class
    {
        if (argument is null)
        {
            throw new ArgumentNullException(name);
        }

        return argument;
    }

    public static string NotNullOrEmpty(string? argument, string? name = null)
    {
        if (string.IsNullOrEmpty(argument))
        {
            throw new ArgumentException("Value cannot be null or empty.", name);
        }

        return argument!;
    }
}
