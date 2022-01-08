// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;

namespace DurableTask;

// TODO: Move to separate file
public interface ITaskActivity
{
    Type InputType { get; }
    Type OutputType { get; }

    Task<object?> RunAsync(TaskActivityContext context, object? input);
}

// TODO: Documentation
public abstract class TaskActivityBase<TInput, TOutput> : ITaskActivity
{
    Type ITaskActivity.InputType => typeof(TInput);
    Type ITaskActivity.OutputType => typeof(TOutput);

    protected virtual Task<TOutput?> OnRunAsync(TaskActivityContext context, TInput? input) => Task.FromResult(this.OnRun(context, input));

    protected virtual TOutput? OnRun(TaskActivityContext context, TInput? input) => throw this.DefaultNotImplementedException();

    async Task<object?> ITaskActivity.RunAsync(TaskActivityContext context, object? input)
    {
        TInput? typedInput = (TInput?)(input ?? default(TInput));
        TOutput? output = await this.OnRunAsync(context, typedInput);
        return output;
    }

    Exception DefaultNotImplementedException()
    {
        return new NotImplementedException($"{this.GetType().Name} needs to override {nameof(this.OnRun)} or {nameof(this.OnRunAsync)} with an implementation.");
    }
}