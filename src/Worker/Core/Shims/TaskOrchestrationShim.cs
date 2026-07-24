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
#pragma warning disable CA1001 // Types that own disposable fields should be disposable -- the base
                               // TaskOrchestration type (defined in DurableTask.Core) has no disposal
                               // hook, so this type cannot implement IDisposable in a way that would ever
                               // be invoked. Instead, wrapperContext is disposed explicitly before each
                               // replacement in Execute(); only the final instance for a given orchestration
                               // execution's lifetime is left for the garbage collector/finalizer to reclaim.
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

        // Dispose the previous execution's wrapper (if any) before replacing it. Each call to Execute
        // represents a new replay/decision task with a fresh wrapper; orchestrator code always runs
        // synchronously within a single Execute call, so the previous wrapper is guaranteed to no longer
        // be in use once we reach this point, and releasing its cached resources (e.g. the SHA1 instance
        // used by NewGuid) here avoids accumulating undisposed instances across replays.
        this.wrapperContext?.Dispose();
        this.wrapperContext = new(innerContext, this.invocationContext, input, this.properties);

        string instanceId = innerContext.OrchestrationInstance.InstanceId;
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
#pragma warning restore CA1001 // Types that own disposable fields should be disposable
