// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Dapr.DurableTask.Client;

/// <summary>
/// A provider for getting <see cref="DurableTaskClient" />.
/// </summary>
/// <remarks>
/// The purpose of this abstraction is that there may be multiple clients registered, so they cannot be DI'd directly.
/// </remarks>
public interface IDurableTaskClientProvider
{
    /// <summary>
    /// Gets the <see cref="DurableTaskClient" /> by name. Throws if the client by the requested name is not found.
    /// </summary>
    /// <param name="name">The name of the client to get or <c>null</c> to get the default client.</param>
    /// <returns>The client.</returns>
    DurableTaskClient GetClient(string? name = null);
}
