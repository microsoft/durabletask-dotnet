// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.ScheduledTasks.Tests.Extension;

public class DurableTaskWorkerBuilderExtensionsTests
{
    [Fact]
    public void UseScheduledTasks_AddsScheduledTasksCapability()
    {
        // Arrange
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(b => b.Services).Returns(services);
        mockBuilder.Setup(b => b.Name).Returns(Options.DefaultName);

        // Act
        mockBuilder.Object.UseScheduledTasks();

        // Assert
        ServiceProvider provider = services.BuildServiceProvider();
        IOptions<GrpcDurableTaskWorkerOptions> options = provider.GetRequiredService<IOptions<GrpcDurableTaskWorkerOptions>>();
        options.Value.Capabilities.Should().Contain(P.WorkerCapability.ScheduledTasks);
    }

    [Fact]
    public void UseScheduledTasks_WithNamedOptions_AddsScheduledTasksCapability()
    {
        // Arrange
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(b => b.Services).Returns(services);
        mockBuilder.Setup(b => b.Name).Returns("CustomName");

        // Act
        mockBuilder.Object.UseScheduledTasks();

        // Assert
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<GrpcDurableTaskWorkerOptions> optionsMonitor = provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskWorkerOptions>>();
        GrpcDurableTaskWorkerOptions options = optionsMonitor.Get("CustomName");
        options.Capabilities.Should().Contain(P.WorkerCapability.ScheduledTasks);
    }
}
