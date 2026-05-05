// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Extensions for <see cref="IDurableTaskWorkerBuilder" />.
/// </summary>
public static partial class DurableTaskWorkerBuilderExtensions
{
    /// <summary>
    /// Adds orchestration middleware to the specified <see cref="IDurableTaskWorkerBuilder"/>.
    /// </summary>
    /// <typeparam name="TMiddleware">The concrete orchestration middleware type.</typeparam>
    /// <param name="builder">The builder to add orchestration middleware to.</param>
    /// <returns>The same <see cref="IDurableTaskWorkerBuilder"/> instance, allowing for method chaining.</returns>
    public static IDurableTaskWorkerBuilder UseOrchestrationMiddleware<TMiddleware>(
        this IDurableTaskWorkerBuilder builder)
        where TMiddleware : class, ITaskOrchestrationMiddleware
    {
        Check.NotNull(builder);
        Type middlewareType = typeof(TMiddleware);
        Check.ConcreteType<ITaskOrchestrationMiddleware>(middlewareType);

        builder.Services.TryAddScoped<TMiddleware>();
        builder.Services.Configure<DurableTaskWorkerMiddlewareOptions>(
            builder.Name,
            options => options.OrchestrationMiddleware.Add(
                TaskOrchestrationMiddlewareRegistration.ForType(middlewareType)));

        return builder;
    }

    /// <summary>
    /// Adds orchestration middleware to the specified <see cref="IDurableTaskWorkerBuilder"/>.
    /// </summary>
    /// <param name="builder">The builder to add orchestration middleware to.</param>
    /// <param name="handler">The orchestration middleware handler.</param>
    /// <returns>The same <see cref="IDurableTaskWorkerBuilder"/> instance, allowing for method chaining.</returns>
    /// <remarks>
    /// The provided delegate instance is reused across invocations. Avoid capturing mutable state in the delegate.
    /// </remarks>
    public static IDurableTaskWorkerBuilder UseOrchestrationMiddleware(
        this IDurableTaskWorkerBuilder builder,
        Func<TaskOrchestrationMiddlewareContext, TaskOrchestrationMiddlewareDelegate, Task> handler)
    {
        Check.NotNull(builder);
        Check.NotNull(handler);

        builder.Services.Configure<DurableTaskWorkerMiddlewareOptions>(
            builder.Name,
            options => options.OrchestrationMiddleware.Add(
                TaskOrchestrationMiddlewareRegistration.ForHandler(handler)));

        return builder;
    }

    /// <summary>
    /// Adds activity middleware to the specified <see cref="IDurableTaskWorkerBuilder"/>.
    /// </summary>
    /// <typeparam name="TMiddleware">The concrete activity middleware type.</typeparam>
    /// <param name="builder">The builder to add activity middleware to.</param>
    /// <returns>The same <see cref="IDurableTaskWorkerBuilder"/> instance, allowing for method chaining.</returns>
    public static IDurableTaskWorkerBuilder UseActivityMiddleware<TMiddleware>(
        this IDurableTaskWorkerBuilder builder)
        where TMiddleware : class, ITaskActivityMiddleware
    {
        Check.NotNull(builder);
        Type middlewareType = typeof(TMiddleware);
        Check.ConcreteType<ITaskActivityMiddleware>(middlewareType);

        builder.Services.TryAddScoped<TMiddleware>();
        builder.Services.Configure<DurableTaskWorkerMiddlewareOptions>(
            builder.Name,
            options => options.ActivityMiddleware.Add(
                TaskActivityMiddlewareRegistration.ForType(middlewareType)));

        return builder;
    }

    /// <summary>
    /// Adds activity middleware to the specified <see cref="IDurableTaskWorkerBuilder"/>.
    /// </summary>
    /// <param name="builder">The builder to add activity middleware to.</param>
    /// <param name="handler">The activity middleware handler.</param>
    /// <returns>The same <see cref="IDurableTaskWorkerBuilder"/> instance, allowing for method chaining.</returns>
    /// <remarks>
    /// The provided delegate instance is reused across invocations. Avoid capturing mutable state in the delegate.
    /// </remarks>
    public static IDurableTaskWorkerBuilder UseActivityMiddleware(
        this IDurableTaskWorkerBuilder builder,
        Func<TaskActivityMiddlewareContext, TaskActivityMiddlewareDelegate, Task> handler)
    {
        Check.NotNull(builder);
        Check.NotNull(handler);

        builder.Services.Configure<DurableTaskWorkerMiddlewareOptions>(
            builder.Name,
            options => options.ActivityMiddleware.Add(
                TaskActivityMiddlewareRegistration.ForHandler(handler)));

        return builder;
    }
}
