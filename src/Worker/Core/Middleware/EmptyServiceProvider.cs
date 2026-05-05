// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.Middleware;

/// <summary>
/// An empty service provider used when no invocation service provider was supplied.
/// </summary>
internal sealed class EmptyServiceProvider : IServiceProvider
{
    EmptyServiceProvider()
    {
    }

    /// <summary>
    /// Gets the singleton empty service provider.
    /// </summary>
    public static EmptyServiceProvider Instance { get; } = new();

    /// <inheritdoc/>
    public object? GetService(Type serviceType) => null;
}
