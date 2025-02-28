// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.Entities;
using Microsoft.DurableTask.Worker.OrchestrationServiceShim.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Worker.OrchestrationServiceShim.Tests;

public class ShimDurableTaskWorkerTests
{
    readonly Mock<IOrchestrationService> orchestrationService = new(MockBehavior.Strict);
    readonly Mock<IDurableTaskFactory> durableTaskFactory = new(MockBehavior.Strict);
    readonly Mock<IServiceProvider> serviceProvider = new(MockBehavior.Strict);
    readonly ShimDurableTaskWorkerOptions options = new();

    [Fact]
    public void Ctor_WithEntityService_EntitiesEnabled()
    {
        // Arrange
        this.durableTaskFactory.As<IDurableTaskFactory2>();
        Mock<IEntityOrchestrationService> entities = this.AddEntitiesToOrchestrationService();
        this.options.EnableEntitySupport = true;

        // Act
        ShimDurableTaskWorker worker = this.CreateWorker();

        // Assert
        worker.Worker.orchestrationService.Should().BeSameAs(this.orchestrationService.Object);
        entities.Verify(m => m.EntityBackendProperties, Times.Once);
    }

    [Fact]
    public void Ctor_WithEntityService_EntitiesDisabled()
    {
        // Arrange
        this.durableTaskFactory.As<IDurableTaskFactory2>();
        this.AddEntitiesToOrchestrationService();
        this.options.EnableEntitySupport = false;

        // Act
        ShimDurableTaskWorker worker = this.CreateWorker();

        // Assert
        worker.Worker.orchestrationService.Should().BeOfType<OrchestrationServiceNoEntities>();
    }

    [Fact]
    public void Ctor_FactoryNoEntitySupport_EntitiesDisabled()
    {
        // Arrange
        this.AddEntitiesToOrchestrationService();
        this.options.EnableEntitySupport = true;

        // Act
        ShimDurableTaskWorker worker = this.CreateWorker();

        // Assert
        worker.Worker.orchestrationService.Should().BeOfType<OrchestrationServiceNoEntities>();
    }

    [Fact]
    public void Ctor_NoEntityService_EntitiesDisabled()
    {
        // Arrange
        this.durableTaskFactory.As<IDurableTaskFactory2>();
        this.options.EnableEntitySupport = true;

        // Act
        ShimDurableTaskWorker worker = this.CreateWorker();

        // Assert
        worker.Worker.orchestrationService.Should().BeSameAs(this.orchestrationService.Object);
    }

    [Fact]
    public async Task Start_StartsInnerWorker()
    {
        // Arrange
        this.orchestrationService.Setup(m => m.TaskOrchestrationDispatcherCount).Returns(1);
        this.orchestrationService.Setup(m => m.MaxConcurrentTaskOrchestrationWorkItems).Returns(1);
        this.orchestrationService.Setup(m => m.TaskActivityDispatcherCount).Returns(1);
        this.orchestrationService.Setup(m => m.MaxConcurrentTaskActivityWorkItems).Returns(1);
        this.orchestrationService.Setup(m => m.StartAsync()).Returns(Task.CompletedTask);
        ShimDurableTaskWorker worker = this.CreateWorker();

        // Act
        await worker.StartAsync(default);

        // Assert
        this.orchestrationService.Verify(m => m.StartAsync(), Times.Once);
    }

    [Fact]
    public async Task Stop_StopsInnerWorker()
    {
        // Arrange
        this.orchestrationService.Setup(m => m.TaskOrchestrationDispatcherCount).Returns(1);
        this.orchestrationService.Setup(m => m.MaxConcurrentTaskOrchestrationWorkItems).Returns(1);
        this.orchestrationService.Setup(m => m.TaskActivityDispatcherCount).Returns(1);
        this.orchestrationService.Setup(m => m.MaxConcurrentTaskActivityWorkItems).Returns(1);
        this.orchestrationService.Setup(m => m.StartAsync()).Returns(Task.CompletedTask);
        this.orchestrationService.Setup(m => m.StopAsync(false)).Returns(Task.CompletedTask);
        ShimDurableTaskWorker worker = this.CreateWorker();
        await worker.StartAsync(default);

        // Act
        await worker.StopAsync(default);

        // Assert
        this.orchestrationService.Verify(m => m.StopAsync(false), Times.Once);
    }

    Mock<IEntityOrchestrationService> AddEntitiesToOrchestrationService()
    {
        Mock<IEntityOrchestrationService> mock = this.orchestrationService.As<IEntityOrchestrationService>();
        mock.Setup(m => m.EntityBackendProperties).Returns(new EntityBackendProperties());
        return mock;
    }

    ShimDurableTaskWorker CreateWorker()
    {
        this.options.Service = this.orchestrationService.Object;
        return new(
            "test",
            this.durableTaskFactory.Object,
            Mock.Of<IOptionsMonitor<ShimDurableTaskWorkerOptions>>(m => m.Get("test") == this.options),
            this.serviceProvider.Object,
            NullLoggerFactory.Instance);
    }
}
