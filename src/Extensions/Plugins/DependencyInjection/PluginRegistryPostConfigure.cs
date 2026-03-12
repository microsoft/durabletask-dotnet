// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Plugins;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask;

/// <summary>
/// Post-configures a <see cref="DurableTaskRegistry"/> to wrap all orchestrator and activity
/// factories with <see cref="PluginOrchestrationWrapper"/> and <see cref="PluginActivityWrapper"/>.
/// This ensures plugin interceptors run transparently for every orchestration and activity execution.
/// </summary>
sealed class PluginRegistryPostConfigure : IPostConfigureOptions<DurableTaskRegistry>
{
    readonly string name;

    public PluginRegistryPostConfigure(string name)
    {
        this.name = name;
    }

    /// <inheritdoc />
    public void PostConfigure(string? name, DurableTaskRegistry registry)
    {
        if (!string.Equals(name, this.name, StringComparison.Ordinal))
        {
            return;
        }

        // Wrap all orchestrator factories so interceptors run on every execution.
        List<KeyValuePair<TaskName, Func<IServiceProvider, ITaskOrchestrator>>> orchestrators = new(registry.Orchestrators);
        foreach (KeyValuePair<TaskName, Func<IServiceProvider, ITaskOrchestrator>> entry in orchestrators)
        {
            Func<IServiceProvider, ITaskOrchestrator> original = entry.Value;
            registry.Orchestrators[entry.Key] = sp =>
            {
                ITaskOrchestrator inner = original(sp);
                PluginPipeline? pipeline = sp.GetService(typeof(PluginPipeline)) as PluginPipeline;
                return pipeline is not null && pipeline.HasOrchestrationInterceptors
                    ? new PluginOrchestrationWrapper(inner, pipeline)
                    : inner;
            };
        }

        // Wrap all activity factories so interceptors run on every execution.
        List<KeyValuePair<TaskName, Func<IServiceProvider, ITaskActivity>>> activities = new(registry.Activities);
        foreach (KeyValuePair<TaskName, Func<IServiceProvider, ITaskActivity>> entry in activities)
        {
            Func<IServiceProvider, ITaskActivity> original = entry.Value;
            registry.Activities[entry.Key] = sp =>
            {
                ITaskActivity inner = original(sp);
                PluginPipeline? pipeline = sp.GetService(typeof(PluginPipeline)) as PluginPipeline;
                return pipeline is not null && pipeline.HasActivityInterceptors
                    ? new PluginActivityWrapper(inner, pipeline)
                    : inner;
            };
        }
    }
}
