// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.Client;

/// <summary>
/// A builder for configuring and adding a <see cref="DurableTaskClient" /> to the service container.
/// </summary>
public interface IDurableTaskClientBuilder
{
    /// <summary>
    /// Gets the name of the client being built.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the service collection.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Gets or sets the target of this builder. The provided type <b>must derive from</b>
    /// <see cref="DurableTaskClient" />. This is the type that will ultimately be built by
    /// <see cref="Build(IServiceProvider)" />.
    /// </summary>
    Type? BuildTarget { get; set; }

    /// <summary>
    /// Builds this instance, yielding the built <see cref="DurableTaskClient" />.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <returns>The built client.</returns>
    DurableTaskClient Build(IServiceProvider serviceProvider);
}
