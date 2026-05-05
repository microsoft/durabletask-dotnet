// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.Worker.Middleware;

/// <summary>
/// Composes and executes orchestration middleware registrations.
/// </summary>
internal sealed class TaskOrchestrationMiddlewarePipeline
{
    readonly TaskOrchestrationMiddlewareDelegate pipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskOrchestrationMiddlewarePipeline"/> class.
    /// </summary>
    /// <param name="registrations">The middleware registrations.</param>
    public TaskOrchestrationMiddlewarePipeline(
        IReadOnlyList<TaskOrchestrationMiddlewareRegistration> registrations)
    {
        Check.NotNull(registrations);

        TaskOrchestrationMiddlewareDelegate current = InvokeBodyAsync;
        for (int i = registrations.Count - 1; i >= 0; i--)
        {
            current = CreateMiddlewareDelegate(registrations[i], current);
        }

        this.pipeline = current;
    }

    /// <summary>
    /// Gets an empty orchestration middleware pipeline.
    /// </summary>
    public static TaskOrchestrationMiddlewarePipeline Empty { get; } =
        new(Array.Empty<TaskOrchestrationMiddlewareRegistration>());

    /// <summary>
    /// Executes the middleware pipeline.
    /// </summary>
    /// <param name="context">The middleware context.</param>
    /// <returns>A task that completes when the pipeline finishes.</returns>
    public Task RunAsync(DefaultTaskOrchestrationMiddlewareContext context)
    {
        Check.NotNull(context);
        return this.pipeline(context);
    }

    static Task InvokeBodyAsync(TaskOrchestrationMiddlewareContext context)
    {
        if (context is DefaultTaskOrchestrationMiddlewareContext concreteContext)
        {
            return concreteContext.InvokeBodyAsync();
        }

        throw new InvalidOperationException(
            $"The orchestration middleware context must be a {nameof(DefaultTaskOrchestrationMiddlewareContext)}.");
    }

    static TaskOrchestrationMiddlewareDelegate CreateMiddlewareDelegate(
        TaskOrchestrationMiddlewareRegistration registration,
        TaskOrchestrationMiddlewareDelegate next)
    {
        if (registration.Handler is { } handler)
        {
            return context => handler(context, next);
        }

        if (registration.MiddlewareType is { } middlewareType)
        {
            return context => InvokeTypeMiddlewareAsync(context, next, middlewareType);
        }

        throw new InvalidOperationException("The orchestration middleware registration is invalid.");
    }

    static Task InvokeTypeMiddlewareAsync(
        TaskOrchestrationMiddlewareContext context,
        TaskOrchestrationMiddlewareDelegate next,
        Type middlewareType)
    {
        if (context is not DefaultTaskOrchestrationMiddlewareContext concreteContext)
        {
            throw new InvalidOperationException(
                $"The orchestration middleware context must be a {nameof(DefaultTaskOrchestrationMiddlewareContext)}.");
        }

        ITaskOrchestrationMiddleware middleware =
            (ITaskOrchestrationMiddleware)concreteContext.Services.GetRequiredService(middlewareType);
        return middleware.InvokeAsync(context, next);
    }
}
