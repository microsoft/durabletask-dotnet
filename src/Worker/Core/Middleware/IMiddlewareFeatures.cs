// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.Middleware;

#pragma warning disable CA1716 // Get and Set are the locked-in middleware feature API shape.

/// <summary>
/// Provides access to host-side middleware features by type.
/// </summary>
/// <remarks>
/// Middleware features are not persisted as part of orchestration history. They are intended for host-specific
/// objects that middleware may need to share while processing a single work item.
/// </remarks>
public interface IMiddlewareFeatures
{
    /// <summary>
    /// Gets a middleware feature by type.
    /// </summary>
    /// <typeparam name="T">The type of feature to retrieve.</typeparam>
    /// <returns>The feature instance, or <c>null</c> if no feature of type <typeparamref name="T"/> was set.</returns>
    T? Get<T>()
        where T : class;

    /// <summary>
    /// Sets a middleware feature by type.
    /// </summary>
    /// <typeparam name="T">The type of feature to set.</typeparam>
    /// <param name="feature">
    /// The feature instance to set. Passing <c>null</c> removes any existing feature of type
    /// <typeparamref name="T"/>.
    /// </param>
    void Set<T>(T? feature)
        where T : class;
}

#pragma warning restore CA1716
