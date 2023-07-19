// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;

namespace System;

/// <summary>
/// Extensions for <see cref="Type"/>.
/// </summary>
static class TypeExtensions
{
    /// <summary>
    /// Checks if a <paramref name="type" /> represents a <see cref="Task" />, <see cref="Task{T}" />,
    /// <see cref="ValueTask" />, or <see cref="ValueTask{T}" />.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns><c>true</c> if a Task or ValueTask, <c>false</c> otherwise.</returns>
    public static bool IsTaskOrValueTask(this Type type)
    {
        Check.NotNull(type);

        if (type == typeof(Task) || type == typeof(ValueTask))
        {
            return true;
        }

        if (type.IsGenericType)
        {
            Type generic = type.GetGenericTypeDefinition();
            if (generic == typeof(Task<>) || generic == typeof(ValueTask<>))
            {
                return true;
            }
        }

        return false;
    }
}
