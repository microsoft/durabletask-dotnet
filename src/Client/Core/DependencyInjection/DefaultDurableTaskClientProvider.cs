// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Default implementation of <see cref="IDurableTaskClientProvider" />.
/// </summary>
/// <param name="clients">The set of clients.</param>
class DefaultDurableTaskClientProvider(IEnumerable<DefaultDurableTaskClientProvider.ClientContainer> clients)
    : IDurableTaskClientProvider
{
    readonly IEnumerable<ClientContainer> clients = Check.NotNull(clients);

    /// <inheritdoc/>
    public DurableTaskClient GetClient(string? name = null)
    {
        name ??= Options.DefaultName;
        ClientContainer? client = this.clients.FirstOrDefault(
            x => string.Equals(name, x.Name, StringComparison.Ordinal)); // options are case sensitive.

        if (client is null)
        {
            string names = string.Join(", ", this.clients.Select(x => $"\"{x.Name}\""));
            throw new ArgumentOutOfRangeException(
                nameof(name), name, $"The value of this argument must be in the set of available clients: [{names}].");
        }

        return client.Client;
    }

    /// <summary>
    /// Container for holding a client in memory.
    /// </summary>
    /// <param name="client">The client.</param>
    internal class ClientContainer(DurableTaskClient client) : IAsyncDisposable
    {
        /// <summary>
        /// Gets the client name.
        /// </summary>
        public string Name => this.Client.Name;

        /// <summary>
        /// Gets the client.
        /// </summary>
        public DurableTaskClient Client { get; } = Check.NotNull(client);

        /// <inheritdoc/>
        public ValueTask DisposeAsync() => this.Client.DisposeAsync();
    }
}
