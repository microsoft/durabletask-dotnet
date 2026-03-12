// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Plugins;
using Microsoft.DurableTask.Plugins.BuiltIn;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask;

/// <summary>
/// Convenience extension methods for adding built-in plugins to the Durable Task worker builder.
/// </summary>
public static class DurableTaskBuiltInPluginExtensions
{
    /// <summary>
    /// Adds the logging plugin that emits structured log events for orchestration and activity lifecycle.
    /// </summary>
    /// <param name="builder">The worker builder.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UseLoggingPlugin(this IDurableTaskWorkerBuilder builder)
    {
        Check.NotNull(builder);

        // Defer plugin creation to when the service provider is available.
        builder.Services.AddSingleton<IDurableTaskPlugin>(sp =>
        {
            ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return new LoggingPlugin(loggerFactory);
        });

        builder.Services.TryAddPluginPipeline();
        return builder;
    }

    /// <summary>
    /// Adds the metrics plugin that tracks execution counts and durations.
    /// </summary>
    /// <param name="builder">The worker builder.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UseMetricsPlugin(this IDurableTaskWorkerBuilder builder)
    {
        return builder.UseMetricsPlugin(new MetricsStore());
    }

    /// <summary>
    /// Adds the metrics plugin with a shared metrics store.
    /// </summary>
    /// <param name="builder">The worker builder.</param>
    /// <param name="store">The metrics store to use.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UseMetricsPlugin(
        this IDurableTaskWorkerBuilder builder,
        MetricsStore store)
    {
        Check.NotNull(builder);
        Check.NotNull(store);

        builder.Services.AddSingleton(store);
        return builder.UsePlugin(new MetricsPlugin(store));
    }

    /// <summary>
    /// Adds the authorization plugin with the specified handler.
    /// </summary>
    /// <param name="builder">The worker builder.</param>
    /// <param name="handler">The authorization handler.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UseAuthorizationPlugin(
        this IDurableTaskWorkerBuilder builder,
        IAuthorizationHandler handler)
    {
        Check.NotNull(builder);
        Check.NotNull(handler);
        return builder.UsePlugin(new AuthorizationPlugin(handler));
    }

    /// <summary>
    /// Adds the validation plugin with the specified validators.
    /// </summary>
    /// <param name="builder">The worker builder.</param>
    /// <param name="validators">The input validators to use.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UseValidationPlugin(
        this IDurableTaskWorkerBuilder builder,
        params IInputValidator[] validators)
    {
        Check.NotNull(builder);
        Check.NotNull(validators);
        return builder.UsePlugin(new ValidationPlugin(validators));
    }

    /// <summary>
    /// Adds the rate limiting plugin with the specified options.
    /// </summary>
    /// <param name="builder">The worker builder.</param>
    /// <param name="configure">Configuration callback for rate limiting options.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UseRateLimitingPlugin(
        this IDurableTaskWorkerBuilder builder,
        Action<RateLimitingOptions>? configure = null)
    {
        Check.NotNull(builder);
        RateLimitingOptions options = new();
        configure?.Invoke(options);
        return builder.UsePlugin(new RateLimitingPlugin(options));
    }

    static void TryAddPluginPipeline(this IServiceCollection services)
    {
        services.TryAddSingleton<PluginPipeline>(sp =>
        {
            IEnumerable<IDurableTaskPlugin> plugins = sp.GetServices<IDurableTaskPlugin>();
            return new PluginPipeline(plugins);
        });
    }
}
