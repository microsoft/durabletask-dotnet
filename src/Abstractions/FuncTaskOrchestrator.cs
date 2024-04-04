// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Helpers for creating <see cref="ITaskOrchestrator" />.
/// </summary>
public static class FuncTaskOrchestrator
{
    /// <summary>
    /// Creates a new <see cref="TaskOrchestrator{TInput, TOutput}" /> with
    /// the provided function as the implementation.
    /// </summary>
    /// <typeparam name="TInput">The input type.</typeparam>
    /// <typeparam name="TOutput">The output type.</typeparam>
    /// <param name="implementation">The orchestrator implementation.</param>
    /// <returns>A new orchestrator.</returns>
    public static TaskOrchestrator<TInput, TOutput> Create<TInput, TOutput>(
        Func<TaskOrchestrationContext, TInput, Task<TOutput>> implementation)
    {
        Check.NotNull(implementation);
        return new Implementation<TInput, TOutput>(implementation);
    }

    /// <summary>
    /// Implementation of <see cref="TaskOrchestrator{TInput, TOutput}"/> that uses
    /// a <see cref="Func{T, TResult}"/> delegate as its implementation.
    /// </summary>
    /// <typeparam name="TInput">The orchestrator input type.</typeparam>
    /// <typeparam name="TOutput">The orchestrator output type.</typeparam>
    /// <param name="implementation">The orchestrator function.</param>
    class Implementation<TInput, TOutput>(Func<TaskOrchestrationContext, TInput, Task<TOutput>> implementation)
        : TaskOrchestrator<TInput, TOutput>
    {
        /// <inheritdoc/>
        public override Task<TOutput> RunAsync(TaskOrchestrationContext context, TInput input)
        {
            return implementation(context, input);
        }
    }
}
