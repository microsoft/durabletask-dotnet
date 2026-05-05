// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Worker.Middleware;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Stores middleware registrations for a durable task worker.
/// </summary>
internal sealed class DurableTaskWorkerMiddlewareOptions
{
    /// <summary>
    /// Gets the ordered orchestration middleware registrations.
    /// </summary>
    public IList<TaskOrchestrationMiddlewareRegistration> OrchestrationMiddleware { get; } =
        new List<TaskOrchestrationMiddlewareRegistration>();

    /// <summary>
    /// Gets the ordered activity middleware registrations.
    /// </summary>
    public IList<TaskActivityMiddlewareRegistration> ActivityMiddleware { get; } =
        new List<TaskActivityMiddlewareRegistration>();
}

/// <summary>
/// Stores a single orchestration middleware registration.
/// </summary>
internal sealed class TaskOrchestrationMiddlewareRegistration
{
    TaskOrchestrationMiddlewareRegistration(
        Type? middlewareType,
        Func<TaskOrchestrationMiddlewareContext, TaskOrchestrationMiddlewareDelegate, Task>? handler)
    {
        this.MiddlewareType = middlewareType;
        this.Handler = handler;
    }

    /// <summary>
    /// Gets the concrete middleware type, when this is a type-based registration.
    /// </summary>
    public Type? MiddlewareType { get; }

    /// <summary>
    /// Gets the middleware handler, when this is a delegate-based registration.
    /// </summary>
    public Func<TaskOrchestrationMiddlewareContext, TaskOrchestrationMiddlewareDelegate, Task>? Handler { get; }

    /// <summary>
    /// Creates a type-based orchestration middleware registration.
    /// </summary>
    /// <param name="middlewareType">The concrete middleware type.</param>
    /// <returns>A type-based orchestration middleware registration.</returns>
    public static TaskOrchestrationMiddlewareRegistration ForType(Type middlewareType)
        => new(Check.NotNull(middlewareType), null);

    /// <summary>
    /// Creates a delegate-based orchestration middleware registration.
    /// </summary>
    /// <param name="handler">The middleware handler.</param>
    /// <returns>A delegate-based orchestration middleware registration.</returns>
    public static TaskOrchestrationMiddlewareRegistration ForHandler(
        Func<TaskOrchestrationMiddlewareContext, TaskOrchestrationMiddlewareDelegate, Task> handler)
        => new(null, Check.NotNull(handler));
}

/// <summary>
/// Stores a single activity middleware registration.
/// </summary>
internal sealed class TaskActivityMiddlewareRegistration
{
    TaskActivityMiddlewareRegistration(
        Type? middlewareType,
        Func<TaskActivityMiddlewareContext, TaskActivityMiddlewareDelegate, Task>? handler)
    {
        this.MiddlewareType = middlewareType;
        this.Handler = handler;
    }

    /// <summary>
    /// Gets the concrete middleware type, when this is a type-based registration.
    /// </summary>
    public Type? MiddlewareType { get; }

    /// <summary>
    /// Gets the middleware handler, when this is a delegate-based registration.
    /// </summary>
    public Func<TaskActivityMiddlewareContext, TaskActivityMiddlewareDelegate, Task>? Handler { get; }

    /// <summary>
    /// Creates a type-based activity middleware registration.
    /// </summary>
    /// <param name="middlewareType">The concrete middleware type.</param>
    /// <returns>A type-based activity middleware registration.</returns>
    public static TaskActivityMiddlewareRegistration ForType(Type middlewareType)
        => new(Check.NotNull(middlewareType), null);

    /// <summary>
    /// Creates a delegate-based activity middleware registration.
    /// </summary>
    /// <param name="handler">The middleware handler.</param>
    /// <returns>A delegate-based activity middleware registration.</returns>
    public static TaskActivityMiddlewareRegistration ForHandler(
        Func<TaskActivityMiddlewareContext, TaskActivityMiddlewareDelegate, Task> handler)
        => new(null, Check.NotNull(handler));
}
