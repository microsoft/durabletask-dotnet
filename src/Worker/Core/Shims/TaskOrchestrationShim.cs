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
/// <para>
/// The base <see cref="TaskOrchestration"/> type (defined in DurableTask.Core) has no disposal hook of its
/// own, so the framework will never call <see cref="Dispose"/> automatically. Callers that construct a
/// <see cref="TaskOrchestrationShim"/> directly (e.g. the gRPC worker processor and the orchestration
/// runner) own its lifetime and are responsible for disposing it once they are done with it -- typically
/// immediately after the single <see cref="Execute"/> call completes, or (for extended sessions) when the
/// cached shim is evicted/removed.
/// </para>
/// </remarks>
partial class TaskOrchestrationShim : TaskOrchestration, IDisposable
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

        // Defensively dispose any previous wrapper before replacing it, in case this shim instance is
        // ever reused across more than one Execute call. Current callers construct a fresh shim per
        // execution and call Execute exactly once, so the actual resource cleanup for this shim's wrapper
        // happens via Dispose() (see the class remarks); this is still safe to do if it ever runs.
        // Execute itself is async (it awaits orchestrator code across yield points), but callers never
        // invoke it again -- concurrently or otherwise -- until a previous Execute call on this shim has
        // fully completed (returned or thrown). That sequential lifecycle guarantee, not synchronous
        // execution, is what ensures a previous wrapper is no longer in use once we reach this point.
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

    /// <summary>
    /// Releases the resources (e.g. the cached <see cref="System.Security.Cryptography.SHA1"/> instance
    /// backing <see cref="TaskOrchestrationContext.NewGuid"/>) held by this shim's current wrapper. Callers
    /// that construct this shim directly are responsible for calling this once they are finished with it,
    /// since the base <see cref="TaskOrchestration"/> type provides no framework-invoked disposal hook.
    /// </summary>
    public void Dispose()
    {
        this.wrapperContext?.Dispose();
    }
}
