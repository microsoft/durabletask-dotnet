// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;

namespace Microsoft.DurableTask;

partial class TaskOrchestrationShim : TaskOrchestration
{
    readonly TaskName name;
    readonly ITaskOrchestrator implementation;
    readonly WorkerContext workerContext;
    readonly OrchestrationRuntimeState runtimeState;

    TaskOrchestrationContextWrapper? wrapperContext;

    public TaskOrchestrationShim(
        OrchestrationInvocationContext context,
        TaskName name,
        ITaskOrchestrator implementation)
    {
        this.workerContext = context.WorkerContext;
        this.runtimeState = context.RuntimeState;
        this.name = name;
        this.implementation = implementation;
    }

    public override async Task<string?> Execute(OrchestrationContext innerContext, string rawInput)
    {
        JsonDataConverterShim converterShim = new(this.workerContext.DataConverter);
        innerContext.MessageDataConverter = converterShim;
        innerContext.ErrorDataConverter = converterShim;

        object? input = this.workerContext.DataConverter.Deserialize(rawInput, this.implementation.InputType);

        this.wrapperContext = new(innerContext, this.name, this.workerContext, this.runtimeState, input);
        object? output = await this.implementation.RunAsync(this.wrapperContext, input);

        // Return the output (if any) as a serialized string.
        return this.workerContext.DataConverter.Serialize(output);
    }

    public override string? GetStatus()
    {
        return this.wrapperContext?.GetDeserializedCustomStatus();
    }

    public override void RaiseEvent(OrchestrationContext context, string name, string input)
    {
        this.wrapperContext?.CompleteExternalEvent(name, input);
    }
}

class TaskOrchestrationShim<TInput, TOutput> : TaskOrchestrationShim
{
    public TaskOrchestrationShim(
        OrchestrationInvocationContext context,
        TaskName name,
        Func<TaskOrchestrationContext, TInput?, Task<TOutput?>> implementation)
        : base(context, name, new FuncTaskOrchestrator<TInput, TOutput>(implementation))
    {
    }
}