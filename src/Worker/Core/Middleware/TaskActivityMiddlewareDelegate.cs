// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.Middleware;

#pragma warning disable CA1711 // Delegate suffix is the locked-in middleware API shape.

/// <summary>
/// A delegate that invokes the next activity middleware in the pipeline.
/// </summary>
/// <param name="context">The activity middleware context.</param>
/// <returns>A task that completes when the middleware pipeline has finished processing.</returns>
public delegate Task TaskActivityMiddlewareDelegate(TaskActivityMiddlewareContext context);

#pragma warning restore CA1711
