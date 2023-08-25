// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;

namespace Microsoft.DurableTask.Entities;

/// <summary>
/// Helpers for task entities.
/// </summary>
static class TaskEntityHelpers
{
    /// <summary>
    /// Unwraps a dispatched result for a <see cref="TaskEntityOperation"/> into a <see cref="ValueTask{Object}"/>.
    /// </summary>
    /// <param name="context">The entity context.</param>
    /// <param name="state">Delegate to resolve new state for the entity.</param>
    /// <param name="result">The result of the operation.</param>
    /// <param name="resultType">The declared type of the result (may be different that actual type).</param>
    /// <returns>A value task which holds the result of the operation and sets state before it completes.</returns>
    public static ValueTask<object?> UnwrapAsync(
        TaskEntityContext context, Func<object?> state, object? result, Type resultType)
    {
        // NOTE: Func<object?> is used for state so that we can lazily resolve it AFTER the operation has ran.
        Check.NotNull(context);
        Check.NotNull(resultType);

        if (typeof(Task).IsAssignableFrom(resultType))
        {
            // Task or Task<T>
            // We assume a declared Task return type is never null.
            return new(UnwrapTask(context, state, (Task)result!, resultType));
        }

        if (resultType == typeof(ValueTask))
        {
            // ValueTask
            // We assume a declared ValueTask return type is never null.
            return UnwrapValueTask(context, state, (ValueTask)result!);
        }

        if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            // ValueTask<T>
            // No inheritance, have to do purely via reflection.
            return UnwrapValueTaskOfT(context, state, result!, resultType);
        }

        context.SetState(state());
        return new(result);
    }

    static async Task<object?> UnwrapTask(TaskEntityContext context, Func<object?> state, Task task, Type declared)
    {
        await task;
        context.SetState(state());
        if (declared.IsGenericType && declared.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return declared.GetProperty("Result", BindingFlags.Public | BindingFlags.Instance).GetValue(task);
        }

        return null;
    }

    static ValueTask<object?> UnwrapValueTask(TaskEntityContext context, Func<object?> state, ValueTask t)
    {
        async Task<object?> Await(ValueTask t)
        {
            await t;
            context.SetState(state());
            return null;
        }

        if (t.IsCompletedSuccessfully)
        {
            context.SetState(state());
            return default;
        }

        return new(Await(t));
    }

    static ValueTask<object?> UnwrapValueTaskOfT(
        TaskEntityContext context, Func<object?> state, object result, Type type)
    {
        // Result and type here must be some form of ValueTask<T>.
        // TODO: can this amount of reflection be avoided?
        if ((bool)type.GetProperty("IsCompletedSuccessfully").GetValue(result))
        {
            context.SetState(state());
            return new(type.GetProperty("Result").GetValue(result));
        }
        else
        {
            Task t = (Task)type.GetMethod("AsTask", BindingFlags.Instance | BindingFlags.Public).Invoke(result, null);
            Type taskType = typeof(Task<>).MakeGenericType(type.GetGenericArguments()[0]);
            return new(UnwrapTask(context, state, t, taskType));
        }
    }
}
