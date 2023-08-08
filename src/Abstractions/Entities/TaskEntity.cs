// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using System.Threading.Tasks;

namespace Microsoft.DurableTask.Entities;

/// <summary>
/// The task entity contract.
/// </summary>
/// <remarks>
/// <para><b>Entity State</b></para>
/// <para>
/// All entity implementations are required to be serializable by the configured <see cref="DataConverter"/>. An entity
/// will have its state deserialized before executing an operation, and then the new state will be the serialized value
/// of the <see cref="ITaskEntity"/> implementation instance post-operation.
/// </para>
/// </remarks>
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
/// An <see cref="ITaskEntity"/> which dispatches its operations to public instance methods or properties.
/// </summary>
/// <remarks>
/// <para><b>Method Binding</b></para>
/// <para>
/// When using this base class, all public methods will be considered valid entity operations.
/// <list type="bullet">
/// <item>Only public methods are considered (private, internal, and protected are not.)</item>
/// <item>Properties are not considered.</item>
/// <item>Operation matching is case insensitive.</item>
/// <item><see cref="NotSupportedException"/> is thrown if no matching public method is found for an operation.</item>
/// <item><see cref="AmbiguousMatchException"/> is thrown if there are multiple public overloads for an operation name.</item>
/// </list>
/// </para>
///
/// <para><b>Parameter Binding</b></para>
/// <para>
/// Operation methods supports parameter binding as follows:
/// <list type="bullet">
/// <item>Can bind to the context by adding a parameter of type <see cref="TaskEntityContext"/>.</item>
/// <item>Can bind to the raw operation by adding a parameter of type <see cref="TaskEntityOperation"/>.</item>
/// <item>Can bind to the operation input directly by adding any parameter which does not match a previously described
/// binding candidate. The operation input, if available, will be deserialized to that type.</item>
/// <item>Default parameters can be used for input to allow for an operation to execute (with the default value) without
/// an input being provided.</item>
/// </list>
///
/// <see cref="InvalidOperationException" /> will be thrown if:
/// <list type="bullet">
/// <item>There is a redundant parameter binding (ie: two context, operation, or input matches)</item>
/// <item>There is an input binding, but no input was provided.</item>
/// <item>There is another unknown type present which does not match context, operation, or input.</item>
/// </list>
/// </para>
///
/// <para><b>Return Value</b></para>
/// <para>
/// Any value returned by the bound method will be returned to the operation caller. Not all callers wait for a return
/// value, such as signal-only callers. The return value is ignored in these cases.
/// </para>
///
/// <para><b>Entity State</b></para>
/// <para>
/// Unchanged from <see cref="ITaskEntity"/>. Entity state is the serialized value of the entity after an operation
/// completes.
/// </para>
/// </remarks>
public abstract class TaskEntity : ITaskEntity
{
    /**
     * TODO:
     * 1. Consider caching a compiled delegate for a given operation name.
     */
    static readonly BindingFlags InstanceBindingFlags
            = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

    /// <inheritdoc/>
    public ValueTask<object?> RunAsync(TaskEntityOperation operation)
    {
        Check.NotNull(operation);
        if (!this.TryDispatchMethod(operation, out object? result, out Type returnType))
        {
            throw new NotSupportedException($"No suitable method found for entity operation '{operation}'.");
        }

        if (typeof(Task).IsAssignableFrom(returnType))
        {
            // Task or Task<T>
            return new(AsGeneric((Task)result!, returnType)); // we assume a declared Task return type is never null.
        }

        if (returnType == typeof(ValueTask))
        {
            // ValueTask
            return AsGeneric((ValueTask)result!); // we assume a declared ValueTask return type is never null.
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            // ValueTask<T>
            return AsGeneric(result!, returnType); // No inheritance, have to do purely via reflection.
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

    static async Task<object?> AsGeneric(Task task, Type declared)
    {
        await task;
        if (declared.IsGenericType && declared.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return declared.GetProperty("Result", BindingFlags.Public | BindingFlags.Instance).GetValue(task);
        }

        return null;
    }

    static ValueTask<object?> AsGeneric(ValueTask t)
    {
        static async Task<object?> Await(ValueTask t)
        {
            await t;
            return null;
        }

        if (t.IsCompletedSuccessfully)
        {
            return default;
        }

        return new(Await(t));
    }

    static ValueTask<object?> AsGeneric(object result, Type type)
    {
        // result and type here must be some form of ValueTask<T>.
        if ((bool)type.GetProperty("IsCompletedSuccessfully").GetValue(result))
        {
            return new(type.GetProperty("Result").GetValue(result));
        }
        else
        {
            Task t = (Task)type.GetMethod("AsTask", BindingFlags.Instance | BindingFlags.Public)
                .Invoke(result, null);
            return new(t.ToGeneric<object?>());
        }
    }

    bool TryDispatchMethod(TaskEntityOperation operation, out object? result, out Type returnType)
    {
        Type t = this.GetType();

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
                if (TryGetInput(parameter, operation, out object? input))
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

        result = method.Invoke(this, inputs);
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
}
