// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Shims;

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
    class Implementation<TInput, TOutput> : TaskOrchestrator<TInput, TOutput>
    {
        readonly Func<TaskOrchestrationContext, TInput, Task<TOutput>> implementation;

        /// <summary>
        /// Initializes a new instance of the <see cref="Implementation{TInput, TOutput}"/> class.
        /// </summary>
        /// <param name="implementation">The orchestrator function.</param>
        public Implementation(Func<TaskOrchestrationContext, TInput, Task<TOutput>> implementation)
        {
            this.implementation = implementation;
        }

        /// <inheritdoc/>
        public override Task<TOutput> RunAsync(TaskOrchestrationContext context, TInput input)
        {
            return this.implementation(context, input);
        }
    }
}
