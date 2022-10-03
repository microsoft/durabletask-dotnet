// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Shims;

/// <summary>
/// Helpers for creating <see cref="ITaskActivity" />.
/// </summary>
public static class FuncTaskActivity
{
    /// <summary>
    /// Creates a new <see cref="TaskActivityBase{TInput, TOutput}" /> with
    /// the provided function as the implementation.
    /// </summary>
    /// <typeparam name="TInput">The input type.</typeparam>
    /// <typeparam name="TOutput">The output type.</typeparam>
    /// <param name="implementation">The activity implementation.</param>
    /// <returns>A new activity.</returns>
    public static TaskActivityBase<TInput, TOutput> Create<TInput, TOutput>(
        Func<TaskActivityContext, TInput?, Task<TOutput?>> implementation)
    {
        return new Implementation<TInput, TOutput>(implementation);
    }

    /// <summary>
    /// Implementation of <see cref="TaskActivityBase{TInput, TOutput}"/> that uses
    /// a <see cref="Func{T, TResult}"/> delegate as its implementation.
    /// </summary>
    /// <typeparam name="TInput">The Activity input type.</typeparam>
    /// <typeparam name="TOutput">The Activity output type.</typeparam>
    class Implementation<TInput, TOutput> : TaskActivityBase<TInput, TOutput>
    {
        readonly Func<TaskActivityContext, TInput?, Task<TOutput?>> implementation;

        /// <summary>
        /// Initializes a new instance of the <see cref="Implementation{TInput, TOutput}"/> class.
        /// </summary>
        /// <param name="implementation">The Activity function.</param>
        public Implementation(Func<TaskActivityContext, TInput?, Task<TOutput?>> implementation)
        {
            this.implementation = implementation;
        }

        /// <inheritdoc/>
        protected override Task<TOutput?> OnRunAsync(TaskActivityContext context, TInput? input)
        {
            return this.implementation(context, input);
        }
    }

}