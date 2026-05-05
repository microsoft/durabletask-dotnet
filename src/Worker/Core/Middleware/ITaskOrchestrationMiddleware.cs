// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.Middleware;

#pragma warning disable CA1716 // next is the locked-in middleware delegate parameter name.

/// <summary>
/// Defines middleware that runs as part of orchestration execution.
/// </summary>
public interface ITaskOrchestrationMiddleware
{
    /// <summary>
    /// Invokes the orchestration middleware.
    /// </summary>
    /// <param name="context">The orchestration middleware context.</param>
    /// <param name="next">The next middleware delegate in the orchestration pipeline.</param>
    /// <returns>A task that completes when the middleware has finished processing.</returns>
    Task InvokeAsync(TaskOrchestrationMiddlewareContext context, TaskOrchestrationMiddlewareDelegate next);
}

#pragma warning restore CA1716
