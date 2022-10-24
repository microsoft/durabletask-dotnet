// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Worker.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Extensions for <see cref="IDurableTaskBuilder" />.
/// </summary>
public static class DurableTaskBuilderExtensions
{
    /// <summary>
    /// Adds tasks to the current builder.
    /// </summary>
    /// <param name="builder">The builder to add tasks to.</param>
    /// <param name="configure">The callback to add tasks.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskBuilder AddTasks(
        this IDurableTaskBuilder builder, Action<DurableTaskRegistry> configure)
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
    public static IDurableTaskBuilder Configure(
        this IDurableTaskBuilder builder, Action<DurableTaskWorkerOptions> configure)
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
    public static IDurableTaskBuilder UseBuildTarget(this IDurableTaskBuilder builder, Type target)
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
    public static IDurableTaskBuilder UseBuildTarget<TTarget>(this IDurableTaskBuilder builder)
        where TTarget : DurableTaskWorker
        => builder.UseBuildTarget(typeof(TTarget));
}
