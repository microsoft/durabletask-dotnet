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
/// <remarks>
/// Initializes a new instance of the <see cref="TaskOrchestrationShim"/> class.
/// </remarks>
/// <param name="invocationContext">The invocation context for this orchestration.</param>
/// <param name="implementation">The orchestration's implementation.</param>
partial class TaskOrchestrationShim(
    OrchestrationInvocationContext invocationContext,
    ITaskOrchestrator implementation) : TaskOrchestration
{
    readonly ITaskOrchestrator implementation = Check.NotNull(implementation);
    readonly OrchestrationInvocationContext invocationContext = Check.NotNull(invocationContext);
    TaskOrchestrationContextWrapper? wrapperContext;

    DataConverter DataConverter => this.invocationContext.Options.DataConverter;

    /// <inheritdoc/>
    public override async Task<string?> Execute(OrchestrationContext innerContext, string rawInput)
    {
        Check.NotNull(innerContext);
        JsonDataConverterShim converterShim = new(this.invocationContext.Options.DataConverter);
        innerContext.MessageDataConverter = converterShim;
        innerContext.ErrorDataConverter = converterShim;

        object? input = this.DataConverter.Deserialize(rawInput, this.implementation.InputType);
        this.wrapperContext = new(innerContext, this.invocationContext, input);

        try
        {
            object? output = await this.implementation.RunAsync(this.wrapperContext, input);

            // Return the output (if any) as a serialized string.
            return this.DataConverter.Serialize(output);
        }
        finally
        {
            // if user code crashed inside a critical section, or did not exit it, do that now
            this.wrapperContext.ExitCriticalSectionIfNeeded();
        }
    }

    /// <inheritdoc/>
    public override string? GetStatus()
    {
        return this.wrapperContext?.GetSerializedCustomStatus();
    }

    /// <inheritdoc/>
    public override void RaiseEvent(OrchestrationContext context, string name, string input)
    {
        this.wrapperContext?.CompleteExternalEvent(name, input);
    }
}
