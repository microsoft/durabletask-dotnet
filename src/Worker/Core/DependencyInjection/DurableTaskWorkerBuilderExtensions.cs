// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Worker.Hosting;
using Microsoft.Extensions.DependencyInjection;
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
    /// Adds <see cref="DurableTaskWorkerWorkItemFilters"/> to the specified <see cref="IDurableTaskWorkerBuilder"/>.
    /// </summary>
    /// <param name="builder">The builder to set the builder target for.</param>
    /// <param name="workItemFilters">The instance of a <see cref="DurableTaskWorkerWorkItemFilters"/> to use.
    /// If <c>null</c>, any previously configured filters will be cleared and filtering will be disabled.</param>
    /// <returns>The same <see cref="IDurableTaskWorkerBuilder"/> instance, allowing for method chaining.</returns>
    /// <remarks>By default, no work item filters are applied and the worker processes all work items.
    /// Use this method with explicit filters to enable filtering, or with <c>null</c> to disable filtering.</remarks>
    public static IDurableTaskWorkerBuilder UseWorkItemFilters(this IDurableTaskWorkerBuilder builder, DurableTaskWorkerWorkItemFilters? workItemFilters)
    {
        Check.NotNull(builder);

        // Use PostConfigure to ensure provided filters override the auto-generated defaults.
        // When null is passed, the filters are cleared to opt out of filtering entirely.
        builder.Services.AddOptions<DurableTaskWorkerWorkItemFilters>(builder.Name)
            .PostConfigure(opts =>
            {
                if (workItemFilters is null)
                {
                    opts.Orchestrations = [];
                    opts.Activities = [];
                    opts.Entities = [];
                }
                else
                {
                    opts.Orchestrations = workItemFilters.Orchestrations;
                    opts.Activities = workItemFilters.Activities;
                    opts.Entities = workItemFilters.Entities;
                }
            });

        return builder;
    }

    /// <summary>
    /// Enables work item filtering by auto-generating filters from the <see cref="DurableTaskRegistry"/>.
    /// When enabled, the backend will only dispatch work items for registered orchestrations, activities,
    /// and entities to this worker.
    /// </summary>
    /// <param name="builder">The builder to set the builder target for.</param>
    /// <returns>The same <see cref="IDurableTaskWorkerBuilder"/> instance, allowing for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Work item filtering can improve efficiency in multi-worker deployments by ensuring each worker
    /// only receives work items it can handle. However, if an orchestration calls a task type
    /// (e.g., an entity, activity, or sub-orchestrator) that is not registered with any connected worker,
    /// the call may hang indefinitely instead of failing with an error.
    /// </para>
    /// <para>
    /// Only use this method when all task types referenced by orchestrations are guaranteed to be
    /// registered with at least one connected worker.
    /// </para>
    /// </remarks>
    public static IDurableTaskWorkerBuilder UseWorkItemFilters(this IDurableTaskWorkerBuilder builder)
    {
        Check.NotNull(builder);

        builder.Services.AddOptions<DurableTaskWorkerWorkItemFilters>(builder.Name)
            .PostConfigure<IOptionsMonitor<DurableTaskRegistry>, IOptionsMonitor<DurableTaskWorkerOptions>>(
                (opts, registryMonitor, workerOptionsMonitor) =>
                {
                    DurableTaskRegistry registry = registryMonitor.Get(builder.Name);
                    DurableTaskWorkerOptions workerOptions = workerOptionsMonitor.Get(builder.Name);
                    DurableTaskWorkerWorkItemFilters generated =
                        DurableTaskWorkerWorkItemFilters.FromDurableTaskRegistry(registry, workerOptions);
                    opts.Orchestrations = generated.Orchestrations;
                    opts.Activities = generated.Activities;
                    opts.Entities = generated.Entities;
                });

        return builder;
    }
}
