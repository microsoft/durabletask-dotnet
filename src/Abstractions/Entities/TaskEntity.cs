// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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

    bool TryDispatchMethod(TaskEntityOperation operation, out object? result)
    {
        Type t = this.GetType();
        MethodInfo? method = t.GetMethod(operation.Name, InstanceBindingFlags);
        if (method is null)
        {
            result = null;
            return false;
        }

        if (method.ReturnType.IsTaskOrValueTask())
        {
            throw new InvalidOperationException($"{typeof(Task)} and {typeof(ValueTask)} return values are not supported");
        }

        ParameterInfo[] parameters = method.GetParameters();
        object?[] inputs = new object[parameters.Length];

        int i = 0;
        bool inputResolved = false;
        bool contextResolved = false;
        bool operationResolved = false;
        foreach (ParameterInfo parameter in parameters)
        {
            if (!contextResolved && parameter.ParameterType == typeof(TaskEntityContext))
            {
                inputs[i] = operation.Context;
                contextResolved = true;
            }
            else if (!operationResolved && parameter.ParameterType == typeof(TaskEntityOperation))
            {
                inputs[i] = operation;
                operationResolved = true;
            }
            else if (!inputResolved)
            {
                inputs[i] = operation.GetInput(parameter.ParameterType);
                inputResolved = true;
            }
            else
            {
                throw new InvalidOperationException($"Entity operation input parameter of '{parameter}' cannot be" +
                    $" resolved. Either this input has already been resolved or is unsupported.");
            }

            i++;
        }

        result = method.Invoke(this, inputs);
        return true;
    }
}
