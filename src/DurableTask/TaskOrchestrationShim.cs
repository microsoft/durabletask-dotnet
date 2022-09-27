// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;

namespace Microsoft.DurableTask;

/// <summary>
/// Shim orchestration implementation that wraps the Durable Task Framework execution engine.
/// </summary>
/// <remarks>
/// This class is intended for use with alternate .NET-based durable task runtimes. It's not intended for use
/// in application code.
/// </remarks>
public partial class TaskOrchestrationShim : TaskOrchestration
{
    readonly TaskName name;
    readonly ITaskOrchestrator implementation;
    readonly WorkerContext workerContext;
    readonly OrchestrationRuntimeState runtimeState;

    TaskOrchestrationContextWrapper? wrapperContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskOrchestrationShim"/> class.
    /// </summary>
    /// <param name="context">Context from the worker to make available to the orchestration runtime.</param>
    /// <param name="name">The name of the orchestration.</param>
    /// <param name="implementation">The orchestration's implementation.</param>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public override string? GetStatus()
    {
        return this.wrapperContext?.GetDeserializedCustomStatus();
    }

    /// <inheritdoc/>
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