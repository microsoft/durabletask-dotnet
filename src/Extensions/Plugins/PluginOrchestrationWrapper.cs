// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Plugins;

/// <summary>
/// Wraps an <see cref="ITaskOrchestrator"/> with the plugin pipeline, invoking
/// interceptors before and after the inner orchestrator runs.
/// </summary>
internal sealed class PluginOrchestrationWrapper : ITaskOrchestrator
{
    readonly ITaskOrchestrator inner;
    readonly PluginPipeline pipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginOrchestrationWrapper"/> class.
    /// </summary>
    /// <param name="inner">The original orchestrator to wrap.</param>
    /// <param name="pipeline">The plugin pipeline.</param>
    public PluginOrchestrationWrapper(ITaskOrchestrator inner, PluginPipeline pipeline)
    {
        this.inner = inner;
        this.pipeline = pipeline;
    }

    /// <inheritdoc />
    public Type InputType => this.inner.InputType;

    /// <inheritdoc />
    public Type OutputType => this.inner.OutputType;

    /// <inheritdoc />
    public async Task<object?> RunAsync(TaskOrchestrationContext context, object? input)
    {
        OrchestrationInterceptorContext interceptorContext = new(
            context.Name,
            context.InstanceId,
            context.IsReplaying,
            input);

        // Only run non-replay interceptors for starting/completing events to avoid duplication.
        if (!context.IsReplaying)
        {
            await this.pipeline.ExecuteOrchestrationStartingAsync(interceptorContext);
        }

        try
        {
            object? result = await this.inner.RunAsync(context, input);

            if (!context.IsReplaying)
            {
                await this.pipeline.ExecuteOrchestrationCompletedAsync(interceptorContext, result);
            }

            return result;
        }
        catch (Exception ex)
        {
            if (!context.IsReplaying)
            {
                await this.pipeline.ExecuteOrchestrationFailedAsync(interceptorContext, ex);
            }

            throw;
        }
    }
}
