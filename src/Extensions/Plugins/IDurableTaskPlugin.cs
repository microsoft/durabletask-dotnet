// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Plugins;

/// <summary>
/// Defines a plugin that can provide reusable activities, orchestrations, and cross-cutting
/// interceptors. Inspired by Temporal's plugin pattern, plugins serve two purposes:
/// <list type="number">
///   <item>Provide <b>built-in activities and orchestrations</b> that users import and register
///   automatically when adding the plugin to a worker.</item>
///   <item>Add <b>cross-cutting interceptors</b> for concerns like logging, metrics, authorization,
///   validation, and rate limiting.</item>
/// </list>
/// </summary>
public interface IDurableTaskPlugin
{
    /// <summary>
    /// Gets the unique name of this plugin.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the orchestration interceptors provided by this plugin.
    /// </summary>
    IReadOnlyList<IOrchestrationInterceptor> OrchestrationInterceptors { get; }

    /// <summary>
    /// Gets the activity interceptors provided by this plugin.
    /// </summary>
    IReadOnlyList<IActivityInterceptor> ActivityInterceptors { get; }

    /// <summary>
    /// Registers the plugin's built-in orchestrations and activities into the given registry.
    /// This is called automatically when the plugin is added to a worker via <c>UsePlugin()</c>.
    /// </summary>
    /// <param name="registry">The task registry to register activities and orchestrations into.</param>
    void RegisterTasks(DurableTaskRegistry registry);
}
