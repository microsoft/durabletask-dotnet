// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Shims;

/// <summary>
/// Helpers for creating <see cref="ITaskOrchestrator" />.
/// </summary>
public static class FuncTaskOrchestrator
{
    /// <summary>
    /// Creates a new <see cref="TaskOrchestratorBase{TInput, TOutput}" /> with
    /// the provided function as the implementation.
    /// </summary>
    /// <typeparam name="TInput">The input type.</typeparam>
    /// <typeparam name="TOutput">The output type.</typeparam>
    /// <param name="implementation">The orchestrator implementation.</param>
    /// <returns>A new orchestrator.</returns>
    public static TaskOrchestratorBase<TInput, TOutput> Create<TInput, TOutput>(
        Func<TaskOrchestrationContext, TInput?, Task<TOutput?>> implementation)
    {
        return new Implementation<TInput, TOutput>(implementation);
    }

    /// <summary>
    /// Implementation of <see cref="TaskOrchestratorBase{TInput, TOutput}"/> that uses
    /// a <see cref="Func{T, TResult}"/> delegate as its implementation.
    /// </summary>
    /// <typeparam name="TInput">The orchestrator input type.</typeparam>
    /// <typeparam name="TOutput">The orchestrator output type.</typeparam>
    class Implementation<TInput, TOutput> : TaskOrchestratorBase<TInput, TOutput>
    {
        readonly Func<TaskOrchestrationContext, TInput?, Task<TOutput?>> implementation;

        /// <summary>
        /// Initializes a new instance of the <see cref="Implementation{TInput, TOutput}"/> class.
        /// </summary>
        /// <param name="implementation">The orchestrator function.</param>
        public Implementation(Func<TaskOrchestrationContext, TInput?, Task<TOutput?>> implementation)
        {
            this.implementation = implementation;
        }

        /// <inheritdoc/>
        protected override Task<TOutput?> OnRunAsync(TaskOrchestrationContext context, TInput? input)
        {
            return this.implementation(context, input);
        }
    }
}
