// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Base class for an orchestration.
/// </summary>
/// <typeparam name="TInput">The orchestration input type.</typeparam>
/// <typeparam name="TOutput">The orchestration output type.</typeparam>
public abstract class Orchestrator<TInput, TOutput> : ITaskOrchestrator
    where TInput : IOrchestrationRequest<TOutput>
{
    /// <inheritdoc/>
    Type ITaskOrchestrator.InputType => typeof(TInput);

    /// <inheritdoc/>
    Type ITaskOrchestrator.OutputType => typeof(TOutput);

    /// <inheritdoc/>
    async Task<object?> ITaskOrchestrator.RunAsync(TaskOrchestrationContext context, object? input)
    {
        Check.NotNull(context, nameof(context));
        OrchestratorHelper.ValidateInput(input, out TInput typedInput);

        return await this.RunAsync(context, typedInput);
    }

    /// <summary>
    /// Override to implement task orchestrator logic.
    /// </summary>
    /// <param name="context">The task orchestrator's context.</param>
    /// <param name="input">The deserialized orchestration input.</param>
    /// <returns>The output of the orchestration as a task.</returns>
    public abstract Task<TOutput> RunAsync(TaskOrchestrationContext context, TInput input);
}

/// <summary>
/// Base class for an orchestration.
/// </summary>
/// <typeparam name="TInput">The orchestration input type.</typeparam>
public abstract class Orchestrator<TInput> : ITaskOrchestrator
    where TInput : IOrchestrationRequest<Unit>
{
    /// <inheritdoc/>
    Type ITaskOrchestrator.InputType => typeof(TInput);

    /// <inheritdoc/>
    Type ITaskOrchestrator.OutputType => typeof(Unit);

    /// <inheritdoc/>
    async Task<object?> ITaskOrchestrator.RunAsync(TaskOrchestrationContext context, object? input)
    {
        Check.NotNull(context, nameof(context));
        OrchestratorHelper.ValidateInput(input, out TInput typedInput);

        await this.RunAsync(context, typedInput);
        return Unit.Value;
    }

    /// <summary>
    /// Override to implement task orchestrator logic.
    /// </summary>
    /// <param name="context">The task orchestrator's context.</param>
    /// <param name="input">The deserialized orchestration input.</param>
    /// <returns>The output of the orchestration as a task.</returns>
    public abstract Task RunAsync(TaskOrchestrationContext context, TInput input);
}

/// <summary>
/// Orchestration implementation helpers.
/// </summary>
static class OrchestratorHelper
{
    /// <summary>
    /// Due to nullable reference types being static analysis only, we need to do our best efforts for validating the
    /// input type, but also give control of nullability to the implementation. It is not ideal, but we do not want to
    /// force 'TInput?' on the RunAsync implementation.
    /// </summary>
    /// <typeparam name="TInput">The input type of the orchestration.</typeparam>
    /// <param name="input">The input object.</param>
    /// <param name="typedInput">The input converted to the desired type.</param>
    public static void ValidateInput<TInput>(object? input, out TInput typedInput)
    {
        if (input is TInput typed)
        {
            // Quick pattern check.
            typedInput = typed;
            return;
        }
        else if (input is not null && typeof(TInput) != input.GetType())
        {
            throw new ArgumentException($"Input type '{input?.GetType()}' does not match expected type '{typeof(TInput)}'.");
        }

        // Input is null and did not match a nullable value type. We do not have enough information to tell if it is
        // valid or not. We will have to defer this decision to the implementation. Additionally, we will coerce a null
        // input to a default value type here. This is to keep the two RunAsync(context, default) overloads to have
        // identical behavior.
        typedInput = default!;
        return;
    }
}
