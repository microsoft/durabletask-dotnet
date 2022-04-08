// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;

namespace Microsoft.DurableTask;

// TODO: Move to separate file
public interface ITaskOrchestrator
{
    Type InputType { get; }
    Type OutputType { get; }

    Task<object?> RunAsync(TaskOrchestrationContext context, object? input);
}

public abstract class TaskOrchestratorBase<TInput, TOutput> : ITaskOrchestrator
{
    Type ITaskOrchestrator.InputType => typeof(TInput);
    Type ITaskOrchestrator.OutputType => typeof(TOutput);

    protected abstract Task<TOutput?> OnRunAsync(TaskOrchestrationContext context, TInput? input);

    async Task<object?> ITaskOrchestrator.RunAsync(TaskOrchestrationContext context, object? input)
    {
        TInput? typedInput = (TInput?)(input ?? default(TInput));
        TOutput? output = await this.OnRunAsync(context, typedInput);
        return output;
    }
}
