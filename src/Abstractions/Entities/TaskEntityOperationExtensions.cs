// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;

namespace Microsoft.DurableTask.Entities;

/// <summary>
/// Extensions for <see cref="TaskEntityOperation"/>.
/// </summary>
public static class TaskEntityOperationExtensions
{
    /**
     * TODO:
     * 1. Consider caching a compiled delegate for a given operation name.
     */
    static readonly BindingFlags InstanceBindingFlags
            = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

    /// <summary>
    /// Dispatches an operation to a type <typeparamref see="T"/>. Entity state will be resolved as
    /// <typeparamref name="T"/> and then the operation dispatch will be attempted on the constructed instance.
    /// </summary>
    /// <typeparam name="T">The type to dispatch to.</typeparam>
    /// <param name="operation">The operation to dispatch.</param>
    /// <returns>The result returned by the method on <typeparamref name="T"/> the operation is dispatched to.</returns>
    /// <exception cref="NotSupportedException">
    /// If no suitable method is found on <typeparamref name="T"/> for dispatch.
    /// </exception>
    public static ValueTask<object?> DispatchAsync<T>(this TaskEntityOperation operation)
    {
        // NOTE: when dispatching this way, we do not support value types as we have no way of capturing the changed
        // value due to defensive copies.
        Check.NotNull(operation);
        object target = operation.GetInput(typeof(T)) ?? Activator.CreateInstance(typeof(T));
        if (!operation.TryDispatch(target, out object? result, out Type returnType))
        {
            throw new NotSupportedException(
                $"No suitable method on {typeof(T)} found for entity operation '{operation}'.");
        }

        return TaskEntityHelpers.UnwrapAsync(operation.Context, () => target, result, returnType);
    }

    /// <summary>
    /// Try to dispatch this operation via reflection to a method on <paramref name="target"/>.
    /// </summary>
    /// <param name="operation">The operation that is being dispatched.</param>
    /// <param name="target">The target to dispatch to.</param>
    /// <param name="result">The result of the dispatch.</param>
    /// <param name="returnType">The declared return type of the dispatched method.</param>
    /// <returns>True if dispatch successful, false otherwise.</returns>
    internal static bool TryDispatch(
        this TaskEntityOperation operation, object target, out object? result, out Type returnType)
    {
        Check.NotNull(operation);
        Check.NotNull(target);
        Type t = target.GetType();

        // Will throw AmbiguousMatchException if more than 1 overload for the method name exists.
        MethodInfo? method = t.GetMethod(operation.Name, InstanceBindingFlags);
        if (method is null)
        {
            result = null;
            returnType = typeof(void);
            return false;
        }

        ParameterInfo[] parameters = method.GetParameters();
        object?[] inputs = new object[parameters.Length];

        int i = 0;
        ParameterInfo? inputResolved = null;
        ParameterInfo? contextResolved = null;
        ParameterInfo? operationResolved = null;
        foreach (ParameterInfo parameter in parameters)
        {
            if (parameter.ParameterType == typeof(TaskEntityContext))
            {
                ThrowIfDuplicateBinding(contextResolved, parameter, "context", operation);
                inputs[i] = operation.Context;
                contextResolved = parameter;
            }
            else if (parameter.ParameterType == typeof(TaskEntityOperation))
            {
                ThrowIfDuplicateBinding(operationResolved, parameter, "operation", operation);
                inputs[i] = operation;
                operationResolved = parameter;
            }
            else
            {
                ThrowIfDuplicateBinding(inputResolved, parameter, "input", operation);
                if (operation.TryGetInput(parameter, out object? input))
                {
                    inputs[i] = input;
                    inputResolved = parameter;
                }
                else
                {
                    throw new InvalidOperationException($"Error dispatching {operation} to '{method}'.\n" +
                        $"There was an error binding parameter '{parameter}'. The operation expected an input value, " +
                        "but no input was provided by the caller.");
                }
            }

            i++;
        }

        result = method.Invoke(target, inputs);
        returnType = method.ReturnType;
        return true;

        static void ThrowIfDuplicateBinding(
            ParameterInfo? existing, ParameterInfo parameter, string bindingConcept, TaskEntityOperation operation)
        {
            if (existing is not null)
            {
                throw new InvalidOperationException($"Error dispatching {operation} to '{parameter.Member}'.\n" +
                    $"Unable to bind {bindingConcept} to '{parameter}' because it has " +
                    $"already been bound to parameter '{existing}'. Please remove the duplicate parameter in method " +
                    $"'{parameter.Member}'.\nEntity operation: {operation}.");
            }
        }
    }

    static bool TryGetInput(this TaskEntityOperation operation, ParameterInfo parameter, out object? input)
    {
        if (!operation.HasInput)
        {
            if (parameter.HasDefaultValue)
            {
                input = parameter.DefaultValue;
                return true;
            }

            input = null;
            return false;
        }

        input = operation.GetInput(parameter.ParameterType);
        return true;
    }
}
