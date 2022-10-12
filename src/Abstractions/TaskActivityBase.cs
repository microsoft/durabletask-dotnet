// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Common interface for task activity implementations.
/// </summary>
/// <remarks>
/// Users should not implement activities using this interface, directly.
/// Instead, <see cref="TaskActivityBase{TInput, TOutput}"/> should be used to implement orchestration activities.
/// </remarks>
public interface ITaskActivity
{
    /// <summary>
    /// Gets the type of the input parameter that this activity accepts.
    /// </summary>
    Type InputType { get; }

    /// <summary>
    /// Gets the type of the return value that this activity produces.
    /// </summary>
    Type OutputType { get; }

    /// <summary>
    /// Invokes the task activity with the specified context and input.
    /// </summary>
    /// <param name="context">The task activity's context.</param>
    /// <param name="input">The task activity's input.</param>
    /// <returns>Returns the activity output as the result of a <see cref="Task"/>.</returns>
    Task<object?> RunAsync(TaskActivityContext context, object? input);
}

/// <summary>
/// Base class for activity implementations.
/// </summary>
/// <remarks>
/// <para>
/// Activities are the basic unit of work in a durable task orchestration. Activities are the tasks that are
/// orchestrated in the business process. For example, you might create an orchestrator to process an order. The tasks
/// may involve checking the inventory, charging the customer, and creating a shipment. Each task would be a separate
/// activity. These activities may be executed serially, in parallel, or some combination of both.
/// </para><para>
/// Unlike task orchestrators, activities aren't restricted in the type of work you can do in them. Activity functions
/// are frequently used to make network calls or run CPU intensive operations. An activity can also return data back to
/// the orchestrator function. The Durable Task runtime guarantees that each called activity function will be executed
/// <strong>at least once</strong> during an orchestration's execution.
/// </para><para>
/// Because activities only guarantee at least once execution, it's recommended that activity logic be implemented as
/// idempotent whenever possible.
/// </para><para>
/// Activities are invoked by orchestrators using one of the <see cref="TaskOrchestrationContext.CallActivityAsync"/>
/// method overloads. Activities that derive from <see cref="TaskActivityBase{TInput, TOutput}"/> can also be invoked
/// using generated extension methods. To participate in code generation, an activity class must be decorated with the
/// <see cref="DurableTaskAttribute"/> attribute. The source generator will then generate a <c>CallMyActivityAsync()</c>
/// extension method for an activity named "MyActivity". The generated input parameter and return value will be derived
/// from <typeparamref name="TInput"/> and <typeparamref name="TOutput"/> respectively.
/// </para>
/// </remarks>
/// <typeparam name="TInput">The type of the input parameter that this activity accepts.</typeparam>
/// <typeparam name="TOutput">The type of the return value that this activity produces.</typeparam>
public abstract class TaskActivityBase<TInput, TOutput> : ITaskActivity
{
    /// <inheritdoc/>
    Type ITaskActivity.InputType => typeof(TInput);

    /// <inheritdoc/>
    Type ITaskActivity.OutputType => typeof(TOutput);

    /// <inheritdoc/>
    async Task<object?> ITaskActivity.RunAsync(TaskActivityContext context, object? input)
    {
        TInput? typedInput = (TInput?)(input ?? default(TInput));
        TOutput? output = await this.OnRunAsync(context, typedInput);
        return output;
    }

    /// <summary>
    /// Override to implement async (non-blocking) task activity logic.
    /// </summary>
    /// <param name="context">Provides access to additional context for the current activity execution.</param>
    /// <param name="input">The deserialized activity input.</param>
    /// <returns>The output of the activity as a task.</returns>
    protected virtual Task<TOutput?> OnRunAsync(TaskActivityContext context, TInput? input) =>
        Task.FromResult(this.OnRun(context, input));

    /// <summary>
    /// Override to implement synchronous (blocking) task activity logic.
    /// </summary>
    /// <inheritdoc cref="OnRunAsync"/>
    protected virtual TOutput? OnRun(TaskActivityContext context, TInput? input) =>
        throw this.DefaultNotImplementedException();

    Exception DefaultNotImplementedException()
    {
        return new NotImplementedException(
            $"{this.GetType().Name} needs to override {nameof(this.OnRun)} or {nameof(this.OnRunAsync)} with an implementation.");
    }
}
