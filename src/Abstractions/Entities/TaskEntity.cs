// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Microsoft.DurableTask.Entities;

/// <summary>
/// The task entity contract.
/// </summary>
/// <remarks>
/// <para><b>Entity State</b></para>
/// <para>
/// The state of an entity can be retrieved and updated via <see cref="TaskEntityOperation.Context"/>.
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
/// <typeparam name="TState">The state type held by this entity.</typeparam>
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
/// Entity state will be hydrated into the <see cref="TaskEntity{TState}.State"/> property. See <see cref="State"/> for
/// more information.
/// </para>
///
/// <para><b>Implicit Operations</b></para>
/// <para>
/// This class supports some operations implicitly. Implicit operations have the lowest priority, after entity and state
/// method dispatching. To override an implicit operation, implement a public method of the same name. Throw
/// <see cref="NotSupportedException"/> from the method to indicate the implicit operation is not supported at all.
/// </para>
/// <para><c>delete</c>: deletes the entity state from storage.</para>
/// </remarks>
public abstract class TaskEntity<TState> : ITaskEntity
{
    /// <summary>
    /// Gets a value indicating whether dispatching operations to <see cref="State"/> is allowed. State dispatch
    /// will only be attempted if entity-level dispatch does not succeed. Default is <c>false</c>. Dispatching to state
    /// follows the same rules as dispatching to this entity.
    /// </summary>
    protected virtual bool AllowStateDispatch => false;

    /// <summary>
    /// Gets or sets the state for this entity.
    /// </summary>
    /// <remarks>
    /// <para><b>Initialization</b></para>
    /// <para>
    /// This will be hydrated as part of <see cref="RunAsync(TaskEntityOperation)"/>. <see cref="InitializeState"/> will
    /// be called when state is <c>null</c> <b>at the start of an operation only</b>.
    /// </para>
    /// <para><b>Persistence</b></para>
    /// <para>
    /// The contents of this property will be persisted to <see cref="TaskEntityState.SetState(object?)"/> at the end
    /// of the operation.
    /// </para>
    /// <para><b>Deletion</b></para>
    /// <para>
    /// Deleting entity state is possible by setting this to <c>null</c>. Setting to default of a value-type will
    /// <b>not</b> perform a delete. This means deleting entity state is only possible for reference types or using <c>?</c>
    /// on a value-type (ie: <c>TaskEntity&lt;int?&gt;</c>).
    /// </para>
    /// </remarks>
    protected TState State { get; set; } = default!;

    /// <summary>
    /// Gets the entity context.
    /// </summary>
    protected TaskEntityContext Context { get; private set; } = null!;

    /// <inheritdoc/>
    public ValueTask<object?> RunAsync(TaskEntityOperation operation)
    {
        Check.NotNull(operation);
        this.Context = operation.Context;
        if (!this.TryGetState(operation, out TState? state, out ValueTask<object?> task))
        {
            return task;
        }

        this.State = state;
        if (!operation.TryDispatch(this, out object? result, out Type returnType)
            && !this.TryDispatchState(operation, out result, out returnType))
        {
            if (TryDispatchImplicit(operation, out task))
            {
                // We do not go into UnwrapAsync for implicit tasks
                return task;
            }

            throw new NotSupportedException($"No suitable method found for entity operation '{operation}'.");
        }

        return TaskEntityHelpers.UnwrapAsync(operation.State, () => this.State, result, returnType);
    }

    /// <summary>
    /// Initializes the entity state. This is only called when there is no current state for this entity.
    /// </summary>
    /// <returns>The entity state.</returns>
    /// <remarks>The default implementation uses <see cref="Activator.CreateInstance()"/>.</remarks>
    protected virtual TState InitializeState()
    {
        if (Nullable.GetUnderlyingType(typeof(TState)) is Type t)
        {
            // Activator.CreateInstance<Nullable<T>>() returns null. To avoid this, we will instantiate via underlying
            // type if it is Nullable<T>. This keeps the experience consistent between value and reference type. If an
            // implementation wants null, they must override this method and explicitly provide null.
            return (TState)Activator.CreateInstance(t);
        }

        return Activator.CreateInstance<TState>();
    }

    static bool TryDispatchImplicit(TaskEntityOperation operation, out ValueTask<object?> result)
    {
        // We do not implement implicit operations via methods because then they would supersede state-dispatching.
        // As such, implicit operations are manually implemented here.
        result = default;
        if (string.Equals(operation.Name, "delete", StringComparison.OrdinalIgnoreCase))
        {
            operation.State.SetState(null);
            return true;
        }

        return false;
    }

    bool TryDispatchState(TaskEntityOperation operation, out object? result, out Type returnType)
    {
        if (!this.AllowStateDispatch)
        {
            result = null;
            returnType = typeof(void);
            return false;
        }

        if (this.State is null)
        {
            throw new InvalidOperationException("Attempting to dispatch to state, but entity state is null.");
        }

        return operation.TryDispatch(this.State, out result, out returnType);
    }

    bool TryGetState(
        TaskEntityOperation operation, [NotNullWhen(true)] out TState? state, out ValueTask<object?> result)
    {
        object? stateObj = null;
        try
        {
            stateObj = operation.State.GetState(typeof(TState));
        }
        catch
        {
            // State might be corrupt, check if this is an implicit operation (such as delete).
            if (TryDispatchImplicit(operation, out result))
            {
                state = default;
                return false;
            }

            throw;
        }

        result = default;
        state = stateObj is null ? this.InitializeState()! : (TState)stateObj;
        return true;
    }
}
