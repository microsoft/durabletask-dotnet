// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Dapr.DurableTask.Client.OrchestrationServiceClientShim.Tests;

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

    [Fact]
    public void EnableEntities_NoBackendSupport_Throws()
    {
        ServiceCollection services = new();
        IOrchestrationServiceClient client = Mock.Of<IOrchestrationServiceClient>();
        services.AddSingleton(client);
        DefaultDurableTaskClientBuilder builder = new(null, services);

        builder.UseOrchestrationService(o => o.EnableEntitySupport = true);

        IServiceProvider provider = services.BuildServiceProvider();
        Action act = () => provider.GetOptions<ShimDurableTaskClientOptions>();
        act.Should().ThrowExactly<OptionsValidationException>()
            .WithMessage("ShimDurableTaskClientOptions.Entities.Queries must not be null when entity support is enabled.");
    }

    [Fact]
    public void EnableEntities_BackendSupport_ExplicitProvider()
    {
        ServiceCollection services = new();
        IOrchestrationServiceClient client = Mock.Of<IOrchestrationServiceClient>();
        services.AddSingleton(client);
        Mock<EntityBackendQueries> mock = new();
        DefaultDurableTaskClientBuilder builder = new(null, services);

        builder.UseOrchestrationService(o =>
        {
            o.EnableEntitySupport = true;
            o.Entities.Queries = mock.Object;
        });

        IServiceProvider provider = services.BuildServiceProvider();
        provider.GetOptions<ShimDurableTaskClientOptions>(); // no-throw
    }

    [Fact]
    public void EnableEntities_BackendSupport_RegisteredService1()
    {
        ServiceCollection services = new();
        IOrchestrationServiceClient client = Mock.Of<IOrchestrationServiceClient>();
        services.AddSingleton(client);
        EntityBackendQueries queries = Mock.Of<EntityBackendQueries>();
        services.AddSingleton(queries);
        DefaultDurableTaskClientBuilder builder = new(null, services);

        builder.UseOrchestrationService(o => o.EnableEntitySupport = true );

        IServiceProvider provider = services.BuildServiceProvider();
        ShimDurableTaskClientOptions options = provider.GetOptions<ShimDurableTaskClientOptions>();
        options.Entities.Queries.Should().Be(queries);
    }

    [Fact]
    public void EnableEntities_BackendSupport_RegisteredService2()
    {
        ServiceCollection services = new();
        Mock<IOrchestrationServiceClient> client = new();
        EntityBackendQueries queries = Mock.Of<EntityBackendQueries>();
        Mock<IEntityOrchestrationService> entities = client.As<IEntityOrchestrationService>();
        entities.Setup(m => m.EntityBackendQueries).Returns(queries);
        services.AddSingleton(client.Object);

        DefaultDurableTaskClientBuilder builder = new(null, services);

        builder.UseOrchestrationService(o => o.EnableEntitySupport = true);

        IServiceProvider provider = services.BuildServiceProvider();
        ShimDurableTaskClientOptions options = provider.GetOptions<ShimDurableTaskClientOptions>();
        options.Entities.Queries.Should().Be(queries);
    }

    [Fact]
    public void EnableEntities_BackendSupport_RegisteredService3()
    {
        ServiceCollection services = new();
        Mock<IOrchestrationServiceClient> client = new();
        EntityBackendQueries queries = Mock.Of<EntityBackendQueries>();
        Mock<IEntityOrchestrationService> entities = client.As<IEntityOrchestrationService>();
        entities.Setup(m => m.EntityBackendQueries).Returns(queries);

        DefaultDurableTaskClientBuilder builder = new(null, services);

        builder.UseOrchestrationService(o =>
        {
            o.Client = client.Object;
            o.EnableEntitySupport = true;
        });

        IServiceProvider provider = services.BuildServiceProvider();
        ShimDurableTaskClientOptions options = provider.GetOptions<ShimDurableTaskClientOptions>();
        options.Entities.Queries.Should().Be(queries);
    }

    [Fact]
    public void EnableEntities_BackendSupport_RegisteredService4()
    {
        ServiceCollection services = new();
        IOrchestrationServiceClient client = Mock.Of<IOrchestrationServiceClient>();
        services.AddSingleton(client);

        EntityBackendQueries queries = Mock.Of<EntityBackendQueries>();
        IEntityOrchestrationService entities = Mock.Of<IEntityOrchestrationService>(m => m.EntityBackendQueries == queries);
        services.AddSingleton(entities);

        DefaultDurableTaskClientBuilder builder = new(null, services);

        builder.UseOrchestrationService(o => o.EnableEntitySupport = true);

        IServiceProvider provider = services.BuildServiceProvider();
        ShimDurableTaskClientOptions options = provider.GetOptions<ShimDurableTaskClientOptions>();
        options.Entities.Queries.Should().Be(queries);
    }

    [Fact]
    public void EnableEntities_BackendSupport_RegisteredService5()
    {
        ServiceCollection services = new();
        IOrchestrationServiceClient client = Mock.Of<IOrchestrationServiceClient>();
        services.AddSingleton(client);

        EntityBackendQueries queries = Mock.Of<EntityBackendQueries>();
        Mock<IOrchestrationService> orchestration = new();
        Mock<IEntityOrchestrationService> entities = orchestration.As<IEntityOrchestrationService>();
        entities.Setup(m => m.EntityBackendQueries).Returns(queries);
        services.AddSingleton(orchestration.Object);

        DefaultDurableTaskClientBuilder builder = new(null, services);

        builder.UseOrchestrationService(o => o.EnableEntitySupport = true);

        IServiceProvider provider = services.BuildServiceProvider();
        ShimDurableTaskClientOptions options = provider.GetOptions<ShimDurableTaskClientOptions>();
        options.Entities.Queries.Should().Be(queries);
    }
}
