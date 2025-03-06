// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Worker.OrchestrationServiceShim.Tests;

public class DurableTaskWorkerBuilderExtensionsTests
{
    [Fact]
    public void UseOrchestrationService_NotSet_Throws()
    {
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new(null, services);
        Action act = () => builder.UseOrchestrationService();
        act.Should().NotThrow();
        builder.BuildTarget.Should().Be(typeof(ShimDurableTaskWorker));

        IServiceProvider provider = services.BuildServiceProvider();
        act = () => provider.GetOptions<ShimDurableTaskWorkerOptions>();

        act.Should().ThrowExactly<OptionsValidationException>()
            .WithMessage("ShimDurableTaskWorkerOptions.Service must not be null.");
    }

    [Fact]
    public void UseOrchestrationService_Service_Sets()
    {
        ServiceCollection services = new();
        IOrchestrationService service = Mock.Of<IOrchestrationService>();
        DefaultDurableTaskWorkerBuilder builder = new(null, services);
        builder.UseOrchestrationService(service);

        IServiceProvider provider = services.BuildServiceProvider();
        ShimDurableTaskWorkerOptions options = provider.GetOptions<ShimDurableTaskWorkerOptions>();

        options.Service.Should().Be(service);
    }

    [Fact]
    public void UseOrchestrationService_FromServices1()
    {
        ServiceCollection services = new();
        IOrchestrationService service = Mock.Of<IOrchestrationService>();
        services.AddSingleton(service);
        DefaultDurableTaskWorkerBuilder builder = new(null, services);

        builder.UseOrchestrationService();

        IServiceProvider provider = services.BuildServiceProvider();
        ShimDurableTaskWorkerOptions options = provider.GetOptions<ShimDurableTaskWorkerOptions>();

        options.Service.Should().Be(service);
    }

    [Fact]
    public void UseOrchestrationService_FromServices2()
    {
        ServiceCollection services = new();
        Mock<IOrchestrationService> mock = new();
        mock.As<IOrchestrationService>();
        services.AddSingleton(mock.As<IOrchestrationService>().Object);
        DefaultDurableTaskWorkerBuilder builder = new(null, services);

        builder.UseOrchestrationService();

        IServiceProvider provider = services.BuildServiceProvider();
        ShimDurableTaskWorkerOptions options = provider.GetOptions<ShimDurableTaskWorkerOptions>();

        options.Service.Should().Be(mock.Object);
    }

    [Fact]
    public void UseOrchestrationService_Callback_Sets()
    {
        ServiceCollection services = new();
        IOrchestrationService service = Mock.Of<IOrchestrationService>();
        DefaultDurableTaskWorkerBuilder builder = new(null, services);
        builder.UseOrchestrationService(opt => opt.Service = service);

        IServiceProvider provider = services.BuildServiceProvider();
        ShimDurableTaskWorkerOptions options = provider.GetOptions<ShimDurableTaskWorkerOptions>();

        options.Service.Should().Be(service);
    }
}
