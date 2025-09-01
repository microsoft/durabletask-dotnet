// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Extensions for <see cref="IDurableTaskClientBuilder" />.
/// </summary>
public static class DurableTaskBuilderExtensions
{
    /// <summary>
    /// Configures the worker options for this builder.
    /// </summary>
    /// <param name="builder">The builder to configure options for.</param>
    /// <param name="configure">The configure callback.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskClientBuilder Configure(
        this IDurableTaskClientBuilder builder, Action<DurableTaskClientOptions> configure)
    {
        builder.Services.Configure(builder.Name, configure);
        return builder;
    }

    /// <summary>
    /// Registers this builders <see cref="DurableTaskClient" /> directly to the service container. This will allow for
    /// directly importing <see cref="DurableTaskClient" />. This can <b>only</b> be used for a single builder. Only
    /// the first call will register.
    /// </summary>
    /// <param name="builder">The builder to register the client directly of.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskClientBuilder RegisterDirectly(this IDurableTaskClientBuilder builder)
    {
        DurableTaskClient GetClient(IServiceProvider services)
        {
            IDurableTaskClientProvider provider = services.GetRequiredService<IDurableTaskClientProvider>();
            return provider.GetClient(builder.Name);
        }

        builder.Services.TryAddSingleton(GetClient);
        return builder;
    }

    /// <summary>
    /// Sets the build target for this builder.
    /// startup.
    /// </summary>
    /// <param name="builder">The builder to set the builder target for.</param>
    /// <param name="target">The type of target to set.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskClientBuilder UseBuildTarget(this IDurableTaskClientBuilder builder, Type target)
    {
        builder.BuildTarget = target;
        return builder;
    }

    /// <summary>
    /// Sets the build target for this builder.
    /// startup.
    /// </summary>
    /// <typeparam name="TTarget">The builder target type.</typeparam>
    /// <param name="builder">The builder to set the builder target for.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskClientBuilder UseBuildTarget<TTarget>(this IDurableTaskClientBuilder builder)
        where TTarget : DurableTaskClient
        => builder.UseBuildTarget(typeof(TTarget));

    /// <summary>
    /// Sets the build target for this builder. Additionally populates default options values for the provided
    /// <typeparamref name="TOptions" />.
    /// </summary>
    /// <typeparam name="TTarget">The builder target type.</typeparam>
    /// <typeparam name="TOptions">The options for this builder.</typeparam>
    /// <param name="builder">The builder to set the builder target for.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskClientBuilder UseBuildTarget<TTarget, TOptions>(this IDurableTaskClientBuilder builder)
        where TTarget : DurableTaskClient
        where TOptions : DurableTaskClientOptions
    {
        builder.UseBuildTarget(typeof(TTarget));
        builder.Services.AddOptions<TOptions>(builder.Name)
            .PostConfigure<IOptionsMonitor<DurableTaskClientOptions>>((options, baseOptions) =>
            {
                DurableTaskClientOptions input = baseOptions.Get(builder.Name);
                input.ApplyTo(options);
            });
        return builder;
    }

    /// <summary>
    /// Sets the default version for this builder. This version will be applied by default to all orchestrations if set.
    /// </summary>
    /// <param name="builder">The builder to set the version for.</param>
    /// <param name="version">The version that will be used as the default version.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskClientBuilder UseDefaultVersion(this IDurableTaskClientBuilder builder, string version)
    {
        builder.Configure(options => options.DefaultVersion = version);
        return builder;
    }

    // Large payload enablement moved to Microsoft.DurableTask.Extensions.AzureBlobPayloads package.
}
