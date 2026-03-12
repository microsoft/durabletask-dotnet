// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Plugins;

/// <summary>
/// Wraps an <see cref="ITaskActivity"/> with the plugin pipeline, invoking
/// interceptors before and after the inner activity runs.
/// </summary>
internal sealed class PluginActivityWrapper : ITaskActivity
{
    readonly ITaskActivity inner;
    readonly PluginPipeline pipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginActivityWrapper"/> class.
    /// </summary>
    /// <param name="inner">The original activity to wrap.</param>
    /// <param name="pipeline">The plugin pipeline.</param>
    public PluginActivityWrapper(ITaskActivity inner, PluginPipeline pipeline)
    {
        this.inner = inner;
        this.pipeline = pipeline;
    }

    /// <inheritdoc />
    public Type InputType => this.inner.InputType;

    /// <inheritdoc />
    public Type OutputType => this.inner.OutputType;

    /// <inheritdoc />
    public async Task<object?> RunAsync(TaskActivityContext context, object? input)
    {
        ActivityInterceptorContext interceptorContext = new(
            context.Name,
            context.InstanceId,
            input);

        await this.pipeline.ExecuteActivityStartingAsync(interceptorContext);

        try
        {
            object? result = await this.inner.RunAsync(context, input);
            await this.pipeline.ExecuteActivityCompletedAsync(interceptorContext, result);
            return result;
        }
        catch (Exception ex)
        {
            await this.pipeline.ExecuteActivityFailedAsync(interceptorContext, ex);
            throw;
        }
    }
}
