// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

public class DefaultDurableTaskClientProviderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("client1")]
    [InlineData("Not-found")] // case sensitive
    [InlineData("client1", "client2")]
    public void GetClient_NotFound_Throws(params string?[] clients)
    {
        clients ??= [];
        DefaultDurableTaskClientProvider provider = new(CreateClients(clients!));
        string allNames = string.Join(", ", clients.Select(x => $"\"{x}\""));

        string message = $"The value of this argument must be in the set of available clients: [{allNames}]."
            + " (Parameter 'name')\nActual value was not-found.";
        Action act = () => provider.GetClient("not-found");
        act.Should().ThrowExactly<ArgumentOutOfRangeException>()
            .WithParameterName("name")
            .WithMessage(message);
    }

    [Theory]
    [InlineData("client1")]
    [InlineData("Client1", "client1")]
    [InlineData("client1", "Client1")]
    [InlineData("client2", "client1", "Client1")]
    [InlineData("Client1", "client1", "client2")]
    public void GetClient_Found_Returns(params string[] clients)
    {
        clients ??= [];
        DefaultDurableTaskClientProvider provider = new(CreateClients(clients));

        DurableTaskClient client = provider.GetClient("client1");
        client.Should().NotBeNull();
        client.Name.Should().Be("client1");
    }

    static List<DefaultDurableTaskClientProvider.ClientContainer> CreateClients(params string[] names)
    {
        return names.Select(n =>
        {
            Mock<DurableTaskClient> client = new(n);
            return new DefaultDurableTaskClientProvider.ClientContainer(client.Object);
        }).ToList();
    }
}
