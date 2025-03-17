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
                MatchStrategy = versionOptions.MatchStrategy,
                FailureStrategy = versionOptions.FailureStrategy,
            };
        });
        return builder;
    }
}
