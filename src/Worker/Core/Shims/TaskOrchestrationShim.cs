// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Microsoft.Extensions.Logging;
using CoreTaskFailedException = DurableTask.Core.Exceptions.TaskFailedException;

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
    readonly ILogger logger;
    readonly IReadOnlyDictionary<string, object?> properties;

    TaskOrchestrationContextWrapper? wrapperContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskOrchestrationShim"/> class.
    /// </summary>
    /// <param name="invocationContext">The invocation context for this orchestration.</param>
    /// <param name="implementation">The orchestration's implementation.</param>
    public TaskOrchestrationShim(
        OrchestrationInvocationContext invocationContext,
        ITaskOrchestrator implementation)
        : this(invocationContext, implementation, new Dictionary<string, object?>())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskOrchestrationShim"/> class.
    /// </summary>
    /// <param name="invocationContext">The invocation context for this orchestration.</param>
    /// <param name="implementation">The orchestration's implementation.</param>
    /// <param name="properties">Configuration for the orchestration.</param>
    public TaskOrchestrationShim(
        OrchestrationInvocationContext invocationContext,
        ITaskOrchestrator implementation,
        IReadOnlyDictionary<string, object?> properties)
    {
        this.invocationContext = Check.NotNull(invocationContext);
        this.implementation = Check.NotNull(implementation);
        this.properties = Check.NotNull(properties);

        this.logger = Logs.CreateWorkerLogger(this.invocationContext.LoggerFactory, "Orchestrations");
    }

    DataConverter DataConverter => this.invocationContext.Options.DataConverter;

    /// <inheritdoc/>
    public override async Task<string?> Execute(OrchestrationContext innerContext, string rawInput)
        {
            Check.NotNull(innerContext);
            JsonDataConverterShim converterShim = new(this.invocationContext.Options.DataConverter);
            innerContext.MessageDataConverter = converterShim;
            innerContext.ErrorDataConverter = converterShim;

            object? input = this.DataConverter.Deserialize(rawInput, this.implementation.InputType);
            this.wrapperContext = new(innerContext, this.invocationContext, input, this.properties);

            string instanceId = innerContext.OrchestrationInstance.InstanceId;
            using IDisposable? scope = this.logger.BeginScope(new Dictionary<string, object?>
            {
                ["InstanceId"] = instanceId,
            });
            if (!innerContext.IsReplaying)
            {
                this.logger.OrchestrationStarted(instanceId, this.invocationContext.Name);
            }

            try
            {
                object? output = await this.implementation.RunAsync(this.wrapperContext, input);

                if (!innerContext.IsReplaying)
                {
                    this.logger.OrchestrationCompleted(instanceId, this.invocationContext.Name);
                }

                // Return the output (if any) as a serialized string.
                return this.DataConverter.Serialize(output);
            }
            catch (TaskFailedException e)
            {
                if (!innerContext.IsReplaying)
                {
                    this.logger.OrchestrationFailed(e, instanceId, this.invocationContext.Name);
                }

                // Convert back to something the Durable Task Framework natively understands so that
                // failure details are correctly propagated.
                throw new CoreTaskFailedException(e.Message, e.InnerException)
                {
                    FailureDetails = new FailureDetails(e,
                        e.FailureDetails.ToCoreFailureDetails(),
                        properties: e.FailureDetails.Properties),
                };
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
