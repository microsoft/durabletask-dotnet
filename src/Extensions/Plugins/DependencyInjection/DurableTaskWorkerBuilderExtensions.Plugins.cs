// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Plugins;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.DurableTask;

/// <summary>
/// Extension methods for adding plugins to the Durable Task worker builder.
/// </summary>
public static class DurableTaskWorkerBuilderExtensionsPlugins
{
    /// <summary>
    /// Adds a plugin to the Durable Task worker. All orchestration and activity interceptors
    /// from the plugin will be invoked during execution.
    /// </summary>
    /// <param name="builder">The worker builder.</param>
    /// <param name="plugin">The plugin to add.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UsePlugin(
        this IDurableTaskWorkerBuilder builder,
        IDurableTaskPlugin plugin)
    {
        Check.NotNull(builder);
        Check.NotNull(plugin);

        builder.Services.AddSingleton(plugin);
        builder.Services.TryAddSingleton<PluginPipeline>(sp =>
        {
            IEnumerable<IDurableTaskPlugin> plugins = sp.GetServices<IDurableTaskPlugin>();
            return new PluginPipeline(plugins);
        });

        return builder;
    }

    /// <summary>
    /// Adds multiple plugins to the Durable Task worker.
    /// </summary>
    /// <param name="builder">The worker builder.</param>
    /// <param name="plugins">The plugins to add.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UsePlugins(
        this IDurableTaskWorkerBuilder builder,
        params IDurableTaskPlugin[] plugins)
    {
        Check.NotNull(builder);
        Check.NotNull(plugins);

        foreach (IDurableTaskPlugin plugin in plugins)
        {
            builder.UsePlugin(plugin);
        }

        return builder;
    }
}
