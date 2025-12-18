// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Worker.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using static Microsoft.DurableTask.Worker.DurableTaskWorkerOptions;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Extensions for <see cref="IDurableTaskWorkerBuilder" />.
/// </summary>
public static class DurableTaskWorkerBuilderExtensions
{
    /// <summary>
    /// Adds tasks to the current builder.
    /// </summary>
    /// <param name="builder">The builder to add tasks to.</param>
    /// <param name="configure">The callback to add tasks.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder AddTasks(
        this IDurableTaskWorkerBuilder builder, Action<DurableTaskRegistry> configure)
    {
        Check.NotNull(builder);
        builder.Services.Configure(builder.Name, configure);
        return builder;
    }

    /// <summary>
    /// Configures the worker options for this builder.
    /// </summary>
    /// <param name="builder">The builder to configure options for.</param>
    /// <param name="configure">The configure callback.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder Configure(
        this IDurableTaskWorkerBuilder builder, Action<DurableTaskWorkerOptions> configure)
    {
        Check.NotNull(builder);
        builder.Services.Configure(builder.Name, configure);
        return builder;
    }

    /// <summary>
    /// Sets the build target for this builder. This is the hosted service which will ultimately be ran on host
    /// startup.
    /// </summary>
    /// <param name="builder">The builder to set the builder target for.</param>
    /// <param name="target">The type of target to set.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UseBuildTarget(this IDurableTaskWorkerBuilder builder, Type target)
    {
        Check.NotNull(builder);
        builder.BuildTarget = target;
        return builder;
    }

    /// <summary>
    /// Sets the build target for this builder. This is the hosted service which will ultimately be ran on host
    /// startup.
    /// </summary>
    /// <typeparam name="TTarget">The builder target type.</typeparam>
    /// <param name="builder">The builder to set the builder target for.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UseBuildTarget<TTarget>(this IDurableTaskWorkerBuilder builder)
        where TTarget : DurableTaskWorker
        => builder.UseBuildTarget(typeof(TTarget));

    /// <summary>
    /// Sets the build target for this builder. This is the hosted service which will ultimately be ran on host
    /// startup.
    /// </summary>
    /// <typeparam name="TTarget">The builder target type.</typeparam>
    /// <typeparam name="TOptions">The options for this builder.</typeparam>
    /// <param name="builder">The builder to set the builder target for.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UseBuildTarget<TTarget, TOptions>(this IDurableTaskWorkerBuilder builder)
        where TTarget : DurableTaskWorker
        where TOptions : DurableTaskWorkerOptions
    {
        builder.UseBuildTarget(typeof(TTarget));
        builder.Services.AddOptions<TOptions>(builder.Name)
            .PostConfigure<IOptionsMonitor<DurableTaskWorkerOptions>>((options, baseOptions) =>
            {
                DurableTaskWorkerOptions input = baseOptions.Get(builder.Name);
                input.ApplyTo(options);
            });
        return builder;
    }

    /// <summary>
    /// Configures the versioning options for this builder.
    /// </summary>
    /// <param name="builder">The builder to set the builder target for.</param>
    /// <param name="versionOptions">The collection of options specified for versioning the worker.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UseVersioning(this IDurableTaskWorkerBuilder builder, VersioningOptions versionOptions)
    {
        Check.NotNull(builder);
        builder.Configure(options =>
        {
            options.Versioning = new VersioningOptions
            {
                Version = versionOptions.Version,
                DefaultVersion = versionOptions.DefaultVersion,
                MatchStrategy = versionOptions.MatchStrategy,
                FailureStrategy = versionOptions.FailureStrategy,
            };
        });
        return builder;
    }

    /// <summary>
    /// Adds an orchestration filter to the specified <see cref="IDurableTaskWorkerBuilder"/>.
    /// </summary>
    /// <param name="builder">The builder to set the builder target for.</param>
    /// <typeparam name="TOrchestrationFilter">The implementation of a <see cref="IOrchestrationFilter"/> that will be bound.</typeparam>
    /// <returns>The same <see cref="IDurableTaskWorkerBuilder"/> instance, allowing for method chaining.</returns>
    [Obsolete("Experimental")]
    public static IDurableTaskWorkerBuilder UseOrchestrationFilter<TOrchestrationFilter>(this IDurableTaskWorkerBuilder builder) where TOrchestrationFilter : class, IOrchestrationFilter
    {
        Check.NotNull(builder);
        builder.Services.AddSingleton<IOrchestrationFilter, TOrchestrationFilter>();
        return builder;
    }

    /// <summary>
    /// Adds an orchestration filter to the specified <see cref="IDurableTaskWorkerBuilder"/>.
    /// </summary>
    /// <param name="builder">The builder to set the builder target for.</param>
    /// <param name="filter">The instance of an <see cref="IOrchestrationFilter"/> to use.</param>
    /// <returns>The same <see cref="IDurableTaskWorkerBuilder"/> instance, allowing for method chaining.</returns>
    [Obsolete("Experimental")]
    public static IDurableTaskWorkerBuilder UseOrchestrationFilter(this IDurableTaskWorkerBuilder builder, IOrchestrationFilter filter)
    {
        Check.NotNull(builder);
        builder.Services.AddSingleton(filter);
        return builder;
    }

    /// <summary>
    /// Registers all task types (activities, orchestrators, and entities) from the specified registry configuration
    /// as services in the dependency injection container. This allows these types to participate in container
    /// validation and enables early detection of dependency resolution issues.
    /// </summary>
    /// <param name="builder">The builder to register tasks for.</param>
    /// <param name="configure">
    /// A callback that provides the registry. This callback will be invoked immediately to extract registered task types.
    /// The same callback will also be registered with <see cref="AddTasks"/> to ensure tasks are registered with the worker.
    /// </param>
    /// <returns>The original builder, for call chaining.</returns>
    /// <remarks>
    /// <para>
    /// Only task types registered via type-based registration methods (e.g., <see cref="DurableTaskRegistry.AddActivity(Type)"/>)
    /// will be registered in the container. Tasks registered via factory methods or singleton instances will not be included.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// builder.Services.AddDurableTaskWorker()
    ///     .AddTasksAsServices(tasks =>
    ///     {
    ///         tasks.AddActivity&lt;MyActivity&gt;();
    ///         tasks.AddOrchestrator&lt;MyOrchestrator&gt;();
    ///     });
    /// </code>
    /// </para>
    /// </remarks>
    public static IDurableTaskWorkerBuilder AddTasksAsServices(
        this IDurableTaskWorkerBuilder builder, Action<DurableTaskRegistry> configure)
    {
        Check.NotNull(builder);
        Check.NotNull(configure);

        // Create a temporary registry to extract the types
        DurableTaskRegistry tempRegistry = new();
        configure(tempRegistry);

        // Register all activity types
        foreach (Type activityType in tempRegistry.ActivityTypes)
        {
            builder.Services.TryAddTransient(activityType);
        }

        // Register all orchestrator types
        foreach (Type orchestratorType in tempRegistry.OrchestratorTypes)
        {
            builder.Services.TryAddTransient(orchestratorType);
        }

        // Register all entity types
        foreach (Type entityType in tempRegistry.EntityTypes)
        {
            builder.Services.TryAddTransient(entityType);
        }

        // Also register the tasks with the builder so they're available to the worker
        builder.AddTasks(configure);

        return builder;
    }
}
