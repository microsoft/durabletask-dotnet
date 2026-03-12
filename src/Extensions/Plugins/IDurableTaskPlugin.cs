// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Plugins;

/// <summary>
/// Defines a plugin that can intercept orchestration and activity lifecycle events.
/// Inspired by Temporal's plugin/interceptor pattern, plugins provide a composable
/// way to add cross-cutting concerns like logging, metrics, authorization, validation,
/// and rate limiting to Durable Task workers and clients.
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
}
