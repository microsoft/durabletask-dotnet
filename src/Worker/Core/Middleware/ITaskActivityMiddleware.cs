// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.Middleware;

#pragma warning disable CA1716 // next is the locked-in middleware delegate parameter name.

/// <summary>
/// Defines middleware that runs as part of activity execution.
/// </summary>
public interface ITaskActivityMiddleware
{
    /// <summary>
    /// Invokes the activity middleware.
    /// </summary>
    /// <param name="context">The activity middleware context.</param>
    /// <param name="next">The next middleware delegate in the activity pipeline.</param>
    /// <returns>A task that completes when the middleware has finished processing.</returns>
    Task InvokeAsync(TaskActivityMiddlewareContext context, TaskActivityMiddlewareDelegate next);
}

#pragma warning restore CA1716
