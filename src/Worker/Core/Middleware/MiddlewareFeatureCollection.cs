// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.Middleware;

#pragma warning disable CA1711 // The name follows the established feature collection pattern.

/// <summary>
/// A default in-memory implementation of <see cref="IMiddlewareFeatures"/>.
/// </summary>
/// <remarks>
/// Features are stored by the type parameter used when calling <see cref="Set{T}"/>. Setting a feature value to
/// <c>null</c> removes the feature, matching the behavior of ASP.NET Core feature collections.
/// </remarks>
public sealed class MiddlewareFeatureCollection : IMiddlewareFeatures
{
    readonly Dictionary<Type, object> features = new();

    /// <inheritdoc/>
    public T? Get<T>()
        where T : class
    {
        return this.features.TryGetValue(typeof(T), out object? feature) ? (T)feature : null;
    }

    /// <inheritdoc/>
    public void Set<T>(T? feature)
        where T : class
    {
        Type type = typeof(T);
        if (feature is null)
        {
            this.features.Remove(type);
            return;
        }

        this.features[type] = feature;
    }
}

#pragma warning restore CA1711
