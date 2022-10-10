// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.Shims;

/// <summary>
/// Shim orchestration implementation that wraps the Durable Task Framework execution engine.
/// </summary>
/// <remarks>
/// This class is intended for use with alternate .NET-based durable task runtimes. It's not intended for use
/// in application code.
/// </remarks>
partial class TaskOrchestrationShim : TaskOrchestration
{
    readonly ITaskOrchestrator implementation;
    readonly OrchestrationInvocationContext invocationContext;
    TaskOrchestrationContextWrapper? wrapperContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskOrchestrationShim"/> class.
    /// </summary>
    /// <param name="invocationContext">The invocation context for this orchestration.</param>
    /// <param name="implementation">The orchestration's implementation.</param>
    public TaskOrchestrationShim(
        OrchestrationInvocationContext invocationContext,
        ITaskOrchestrator implementation)
    {
        this.invocationContext = invocationContext;
        this.implementation = implementation;
    }

    DataConverter DataConverter => this.invocationContext.Options.DataConverter;

    ILoggerFactory LoggerFactory => this.invocationContext.LoggerFactory;

    /// <inheritdoc/>
    public override async Task<string?> Execute(OrchestrationContext innerContext, string rawInput)
    {
        JsonDataConverterShim converterShim = new(this.invocationContext.Options.DataConverter);
        innerContext.MessageDataConverter = converterShim;
        innerContext.ErrorDataConverter = converterShim;

        object? input = this.DataConverter.Deserialize(
            rawInput, this.implementation.InputType);

        ILogger contextLogger = this.LoggerFactory.CreateLogger("Microsoft.DurableTask");
        this.wrapperContext = new(innerContext, this.invocationContext, contextLogger, input);
        object? output = await this.implementation.RunAsync(this.wrapperContext, input);

        // Return the output (if any) as a serialized string.
        return this.DataConverter.Serialize(output);
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