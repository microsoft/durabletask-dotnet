// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Default implementation of <see cref="IDurableTaskClientProvider" />.
/// </summary>
class DefaultDurableTaskClientProvider : IDurableTaskClientProvider
{
    readonly IEnumerable<DurableTaskClient> clients;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultDurableTaskClientProvider"/> class.
    /// </summary>
    /// <param name="clients">The set of clients.</param>
    public DefaultDurableTaskClientProvider(IEnumerable<DurableTaskClient> clients)
    {
        this.clients = clients;
    }

    /// <inheritdoc/>
    public DurableTaskClient GetClient(string? name = null)
    {
        name ??= Options.DefaultName;
        DurableTaskClient? client = this.clients.FirstOrDefault(
            x => string.Equals(name, x.Name, StringComparison.Ordinal)); // options are case sensitive.

        if (client is null)
        {
            string names = string.Join(", ", this.clients.Select(x => $"\"{x.Name}\""));
            throw new ArgumentOutOfRangeException(
                nameof(name), name, $"The value of this argument must be in the set of available clients: [{names}].");
        }

        return client;
    }
}
