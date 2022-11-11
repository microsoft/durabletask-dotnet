// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Default implementation of <see cref="IDurableTaskClientProvider" />.
/// </summary>
class DefaultDurableTaskClientProvider : IDurableTaskClientProvider
{
    readonly IEnumerable<ClientContainer> clients;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultDurableTaskClientProvider"/> class.
    /// </summary>
    /// <param name="clients">The set of clients.</param>
    public DefaultDurableTaskClientProvider(IEnumerable<ClientContainer> clients)
    {
        this.clients = clients;
    }

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
    internal class ClientContainer
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClientContainer"/> class.
        /// </summary>
        /// <param name="client">The client.</param>
        public ClientContainer(DurableTaskClient client)
        {
            this.Client = Check.NotNull(client);
        }

        /// <summary>
        /// Gets the client name.
        /// </summary>
        public string Name => this.Client.Name;

        /// <summary>
        /// Gets the client.
        /// </summary>
        public DurableTaskClient Client { get; }
    }
}
