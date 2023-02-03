// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Client.OrchestrationServiceClientShim.Tests;

public class DurableTaskClientBuilderExtensionsTests
{
    [Fact]
    public void UseOrchestrationService_NotSet_Throws()
    {
        ServiceCollection services = new();
        DefaultDurableTaskClientBuilder builder = new(null, services);
        Action act = () => builder.UseOrchestrationService();
        act.Should().NotThrow();
        builder.BuildTarget.Should().Be(typeof(ShimDurableTaskClient));

        IServiceProvider provider = services.BuildServiceProvider();
        act = () => provider.GetOptions<ShimDurableTaskClientOptions>();

        act.Should().ThrowExactly<OptionsValidationException>()
            .WithMessage("ShimDurableTaskClientOptions.Client must not be null.");
    }

    [Fact]
    public void UseOrchestrationService_Client_Sets()
    {
        ServiceCollection services = new();
        IOrchestrationServiceClient client = Mock.Of<IOrchestrationServiceClient>();
        DefaultDurableTaskClientBuilder builder = new(null, services);
        builder.UseOrchestrationService(client);

        IServiceProvider provider = services.BuildServiceProvider();
        ShimDurableTaskClientOptions options = provider.GetOptions<ShimDurableTaskClientOptions>();

        options.Client.Should().Be(client);
    }

    [Fact]
    public void UseOrchestrationService_FromServices1()
    {
        ServiceCollection services = new();
        IOrchestrationServiceClient client = Mock.Of<IOrchestrationServiceClient>();
        services.AddSingleton(client);
        DefaultDurableTaskClientBuilder builder = new(null, services);

        builder.UseOrchestrationService();

        IServiceProvider provider = services.BuildServiceProvider();
        ShimDurableTaskClientOptions options = provider.GetOptions<ShimDurableTaskClientOptions>();

        options.Client.Should().Be(client);
    }

    [Fact]
    public void UseOrchestrationService_FromServices2()
    {
        ServiceCollection services = new();
        Mock<IOrchestrationServiceClient> mock = new();
        mock.As<IOrchestrationService>();
        services.AddSingleton(mock.As<IOrchestrationService>().Object);
        DefaultDurableTaskClientBuilder builder = new(null, services);

        builder.UseOrchestrationService();

        IServiceProvider provider = services.BuildServiceProvider();
        ShimDurableTaskClientOptions options = provider.GetOptions<ShimDurableTaskClientOptions>();

        options.Client.Should().Be(mock.Object);
    }

    [Fact]
    public void UseOrchestrationService_Callback_Sets()
    {
        ServiceCollection services = new();
        IOrchestrationServiceClient client = Mock.Of<IOrchestrationServiceClient>();
        DefaultDurableTaskClientBuilder builder = new(null, services);
        builder.UseOrchestrationService(opt => opt.Client = client);

        IServiceProvider provider = services.BuildServiceProvider();
        ShimDurableTaskClientOptions options = provider.GetOptions<ShimDurableTaskClientOptions>();

        options.Client.Should().Be(client);
    }
}
