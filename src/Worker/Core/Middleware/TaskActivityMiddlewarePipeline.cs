// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.Worker.Middleware;

/// <summary>
/// Composes and executes activity middleware registrations.
/// </summary>
internal sealed class TaskActivityMiddlewarePipeline
{
    readonly TaskActivityMiddlewareDelegate pipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskActivityMiddlewarePipeline"/> class.
    /// </summary>
    /// <param name="registrations">The middleware registrations.</param>
    public TaskActivityMiddlewarePipeline(IReadOnlyList<TaskActivityMiddlewareRegistration> registrations)
    {
        Check.NotNull(registrations);

        TaskActivityMiddlewareDelegate current = InvokeBodyAsync;
        for (int i = registrations.Count - 1; i >= 0; i--)
        {
            current = CreateMiddlewareDelegate(registrations[i], current);
        }

        this.pipeline = current;
    }

    /// <summary>
    /// Gets an empty activity middleware pipeline.
    /// </summary>
    public static TaskActivityMiddlewarePipeline Empty { get; } =
        new(Array.Empty<TaskActivityMiddlewareRegistration>());

    /// <summary>
    /// Executes the middleware pipeline.
    /// </summary>
    /// <param name="context">The middleware context.</param>
    /// <returns>A task that completes when the pipeline finishes.</returns>
    public Task RunAsync(DefaultTaskActivityMiddlewareContext context)
    {
        Check.NotNull(context);
        return this.pipeline(context);
    }

    static Task InvokeBodyAsync(TaskActivityMiddlewareContext context)
    {
        if (context is DefaultTaskActivityMiddlewareContext concreteContext)
        {
            return concreteContext.InvokeBodyAsync();
        }

        throw new InvalidOperationException(
            $"The activity middleware context must be a {nameof(DefaultTaskActivityMiddlewareContext)}.");
    }

    static TaskActivityMiddlewareDelegate CreateMiddlewareDelegate(
        TaskActivityMiddlewareRegistration registration,
        TaskActivityMiddlewareDelegate next)
    {
        if (registration.Handler is { } handler)
        {
            return context => handler(context, next);
        }

        if (registration.MiddlewareType is { } middlewareType)
        {
            return context => InvokeTypeMiddlewareAsync(context, next, middlewareType);
        }

        throw new InvalidOperationException("The activity middleware registration is invalid.");
    }

    static Task InvokeTypeMiddlewareAsync(
        TaskActivityMiddlewareContext context,
        TaskActivityMiddlewareDelegate next,
        Type middlewareType)
    {
        if (context is not DefaultTaskActivityMiddlewareContext concreteContext)
        {
            throw new InvalidOperationException(
                $"The activity middleware context must be a {nameof(DefaultTaskActivityMiddlewareContext)}.");
        }

        ITaskActivityMiddleware middleware =
            (ITaskActivityMiddleware)concreteContext.Services.GetRequiredService(middlewareType);
        return middleware.InvokeAsync(context, next);
    }
}
