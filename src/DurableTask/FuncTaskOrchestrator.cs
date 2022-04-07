// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;

namespace DurableTask;

/// <summary>
/// Implementation of <see cref="TaskOrchestratorBase{TInput, TOutput}"/> that uses
/// a <see cref="Func{T, TResult}"/> delegate as its implementation.
/// </summary>
/// <typeparam name="TInput">The orchestrator input type.</typeparam>
/// <typeparam name="TOutput">The orchestrator output type.</typeparam>
public class FuncTaskOrchestrator<TInput, TOutput> : TaskOrchestratorBase<TInput, TOutput>
{
    readonly Func<TaskOrchestrationContext, TInput?, Task<TOutput?>> implementation;

    /// <summary>
    /// Initializes a new instance of the <see cref="FuncTaskOrchestrator{TInput, TOutput}"/> class.
    /// </summary>
    /// <param name="implementation">The orchestrator function.</param>
    public FuncTaskOrchestrator(Func<TaskOrchestrationContext, TInput?, Task<TOutput?>> implementation)
    {
        this.implementation = implementation;
    }

    /// <inheritdoc/>
    protected override Task<TOutput?> OnRunAsync(TaskOrchestrationContext context, TInput? input)
    {
        return this.implementation(context, input);
    }
}
