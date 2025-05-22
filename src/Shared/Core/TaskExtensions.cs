// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Dapr.DurableTask;

namespace System;

/// <summary>
/// Extensions for <see cref="Task"/> and <see cref="ValueTask" />.
/// </summary>
static class TaskExtensions
{
    /// <summary>
    /// Converts a <see cref="Task"/> to a generic <see cref="Task{T}"/>.
    /// </summary>
    /// <typeparam name="T">The generic type param to convert to.</typeparam>
    /// <param name="task">The task to convert.</param>
    /// <returns>The converted task.</returns>
    public static async Task<T?> ToGeneric<T>(this Task task)
    {
        await Check.NotNull(task);

        Type t = task.GetType();
        if (t.IsGenericType)
        {
            return (T)t.GetProperty("Result", BindingFlags.Public | BindingFlags.Instance)!.GetValue(task)!;
        }

        return default;
    }

    /// <summary>
    /// Converts a <see cref="ValueTask"/> to a <see cref="ValueTask{T}"/>.
    /// </summary>
    /// <typeparam name="T">The generic type param to convert to.</typeparam>
    /// <param name="task">The value task to convert.</param>
    /// <returns>The converted value task.</returns>
    public static async ValueTask<T?> ToGeneric<T>(this ValueTask task)
    {
        await task;
        return default;
    }
}
