// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Worker.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.DependencyInjection;

/// <summary>
/// A special <see cref="IServiceProvider" /> for orchestrations which will allow for resolving a <b>limited</b> set of
/// services. These services are carefully chosen to ensure they are orchestration-safe.
/// </summary>
/// <remarks>
/// In most cases, using injected services in an orchestration will lead to replay-idempotence issues, resulting in
/// hard to diagnose issues. To avoid this, we intentionally block most services from being injected and only allow a
/// very small set of pre-approved services, where we have wrapped it in a idempotent safe version.
///
/// Supported services:
/// - ILogger{T}. A replay-safe logger.
/// - ILoggerFactory. Provides replay-safe loggers.
/// </remarks>
public sealed class OrchestrationServiceProvider : IServiceProvider
{
    readonly Lazy<ILoggerFactory> loggerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestrationServiceProvider"/> class.
    /// </summary>
    /// <param name="innerProvider">The inner service provider.</param>
    internal OrchestrationServiceProvider(IServiceProvider innerProvider)
    {
        Check.NotNull(innerProvider);
        this.loggerFactory = new(() =>
        {
            ILoggerFactory inner = innerProvider.GetRequiredService<ILoggerFactory>();
            return new OrchestrationLoggerFactory(inner);
        });
    }

    /// <inheritdoc/>
    public object GetService(Type serviceType)
    {
        Check.NotNull(serviceType);
        if (serviceType == typeof(ILoggerFactory))
        {
            return this.loggerFactory.Value;
        }

        if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(ILogger<>))
        {
            Type closed = typeof(Logger<>).MakeGenericType(serviceType.GenericTypeArguments.First());
            return Activator.CreateInstance(closed, this.loggerFactory.Value);
        }

        // TODO: Create and include a support link for more information.
        throw new NotSupportedException($"Type {serviceType} is not supported in orchestrations.");
    }
}
