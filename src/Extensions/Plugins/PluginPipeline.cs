// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Plugins;

/// <summary>
/// Manages the pipeline of registered plugins and executes interceptors in order.
/// </summary>
public sealed class PluginPipeline
{
    readonly IReadOnlyList<IDurableTaskPlugin> plugins;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginPipeline"/> class.
    /// </summary>
    /// <param name="plugins">The plugins to include in the pipeline.</param>
    public PluginPipeline(IEnumerable<IDurableTaskPlugin> plugins)
    {
        Check.NotNull(plugins);
        this.plugins = plugins.ToList();
    }

    /// <summary>
    /// Gets the registered plugins.
    /// </summary>
    public IReadOnlyList<IDurableTaskPlugin> Plugins => this.plugins;

    /// <summary>
    /// Executes all orchestration-starting interceptors in registration order.
    /// </summary>
    /// <param name="context">The orchestration interceptor context.</param>
    /// <returns>A task that completes when all interceptors have run.</returns>
    public async Task ExecuteOrchestrationStartingAsync(OrchestrationInterceptorContext context)
    {
        foreach (IDurableTaskPlugin plugin in this.plugins)
        {
            foreach (IOrchestrationInterceptor interceptor in plugin.OrchestrationInterceptors)
            {
                await interceptor.OnOrchestrationStartingAsync(context);
            }
        }
    }

    /// <summary>
    /// Executes all orchestration-completed interceptors in registration order.
    /// </summary>
    /// <param name="context">The orchestration interceptor context.</param>
    /// <param name="result">The orchestration result.</param>
    /// <returns>A task that completes when all interceptors have run.</returns>
    public async Task ExecuteOrchestrationCompletedAsync(OrchestrationInterceptorContext context, object? result)
    {
        foreach (IDurableTaskPlugin plugin in this.plugins)
        {
            foreach (IOrchestrationInterceptor interceptor in plugin.OrchestrationInterceptors)
            {
                await interceptor.OnOrchestrationCompletedAsync(context, result);
            }
        }
    }

    /// <summary>
    /// Executes all orchestration-failed interceptors in registration order.
    /// </summary>
    /// <param name="context">The orchestration interceptor context.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <returns>A task that completes when all interceptors have run.</returns>
    public async Task ExecuteOrchestrationFailedAsync(OrchestrationInterceptorContext context, Exception exception)
    {
        foreach (IDurableTaskPlugin plugin in this.plugins)
        {
            foreach (IOrchestrationInterceptor interceptor in plugin.OrchestrationInterceptors)
            {
                await interceptor.OnOrchestrationFailedAsync(context, exception);
            }
        }
    }

    /// <summary>
    /// Executes all activity-starting interceptors in registration order.
    /// </summary>
    /// <param name="context">The activity interceptor context.</param>
    /// <returns>A task that completes when all interceptors have run.</returns>
    public async Task ExecuteActivityStartingAsync(ActivityInterceptorContext context)
    {
        foreach (IDurableTaskPlugin plugin in this.plugins)
        {
            foreach (IActivityInterceptor interceptor in plugin.ActivityInterceptors)
            {
                await interceptor.OnActivityStartingAsync(context);
            }
        }
    }

    /// <summary>
    /// Executes all activity-completed interceptors in registration order.
    /// </summary>
    /// <param name="context">The activity interceptor context.</param>
    /// <param name="result">The activity result.</param>
    /// <returns>A task that completes when all interceptors have run.</returns>
    public async Task ExecuteActivityCompletedAsync(ActivityInterceptorContext context, object? result)
    {
        foreach (IDurableTaskPlugin plugin in this.plugins)
        {
            foreach (IActivityInterceptor interceptor in plugin.ActivityInterceptors)
            {
                await interceptor.OnActivityCompletedAsync(context, result);
            }
        }
    }

    /// <summary>
    /// Executes all activity-failed interceptors in registration order.
    /// </summary>
    /// <param name="context">The activity interceptor context.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <returns>A task that completes when all interceptors have run.</returns>
    public async Task ExecuteActivityFailedAsync(ActivityInterceptorContext context, Exception exception)
    {
        foreach (IDurableTaskPlugin plugin in this.plugins)
        {
            foreach (IActivityInterceptor interceptor in plugin.ActivityInterceptors)
            {
                await interceptor.OnActivityFailedAsync(context, exception);
            }
        }
    }
}
