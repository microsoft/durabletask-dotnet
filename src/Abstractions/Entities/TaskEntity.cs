// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Reflection;

namespace Microsoft.DurableTask.Entities;

/// <summary>
/// The task entity contract.
/// </summary>
public interface ITaskEntity
{
    /// <summary>
    /// Runs an operation for this entity.
    /// </summary>
    /// <param name="operation">The operation to run.</param>
    /// <returns>The response to the caller, if any.</returns>
    ValueTask<object?> RunAsync(TaskEntityOperation operation);
}

/// <summary>
/// A <see cref="ITaskEntity"/> which dispatches its operations to public instance methods or properties.
/// </summary>
public abstract class TaskEntity : ITaskEntity
{
    /**
     * TODO:
     * 1. Evaluate if Task and ValueTask support is necessary. If so, also support "Async" method name suffix.
     * 2. Consider caching a compiled delegate for a given operation name.
     */
    static readonly BindingFlags InstanceBindingFlags
            = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

    /// <inheritdoc/>
    public ValueTask<object?> RunAsync(TaskEntityOperation operation)
    {
        Check.NotNull(operation);
        if (!this.TryDispatchMethod(operation, out object? result))
        {
            throw new NotSupportedException($"Entity operation {operation} is not supported.");
        }

        return new(result);
    }

    static bool TryGetInput(ParameterInfo parameter, TaskEntityOperation operation, out object? input)
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

    bool TryDispatchMethod(TaskEntityOperation operation, out object? result)
    {
        static void CheckDuplicateBinding(
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

        Type t = this.GetType();
        MethodInfo? method = t.GetMethod(operation.Name, InstanceBindingFlags);
        if (method is null)
        {
            result = null;
            return false;
        }

        if (method.ReturnType.IsTaskOrValueTask())
        {
            throw new InvalidOperationException($"Error dispatching {operation} to '{method}'.\n" +
                $"{typeof(Task)} and {typeof(ValueTask)} return values are not supported. Return type found: " +
                $"{method.ReturnType}.");
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
                CheckDuplicateBinding(contextResolved, parameter, "context", operation);
                inputs[i] = operation.Context;
                contextResolved = parameter;
            }
            else if (parameter.ParameterType == typeof(TaskEntityOperation))
            {
                CheckDuplicateBinding(operationResolved, parameter, "operation", operation);
                inputs[i] = operation;
                operationResolved = parameter;
            }
            else if (TryGetInput(parameter, operation, out object? input))
            {
                CheckDuplicateBinding(inputResolved, parameter, "input", operation);
                inputs[i] = input;
                inputResolved = parameter;
            }
            else
            {
                throw new InvalidOperationException($"Error dispatching {operation} to '{method}'.\n" +
                    $"There was an error binding parameter '{parameter}'. Either the type is not supported or no " +
                    $"input was provided for this parameter.\nInput provided: {operation.HasInput}.");
            }

            i++;
        }

        result = method.Invoke(this, inputs);
        return true;
    }
}
