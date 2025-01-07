// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Grpc.Net.Client;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Grpc;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace DurableTask.Extensions.Azure.Tests;

public class DurableTaskSchedulerExtensionsTests
{
    private const string ValidEndpoint = "myaccount.westus3.durabletask.io";
    private const string ValidTaskHub = "testhub";

    [Fact]
    public void UseDurableTaskScheduler_Worker_WithEndpointAndCredential_ShouldConfigureCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockBuilder = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        var credential = new DefaultAzureCredential();

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential);

        // Assert - Verify that the options were registered
        var provider = services.BuildServiceProvider();
        var options = provider.GetService<IOptions<GrpcDurableTaskWorkerOptions>>();
        options.Should().NotBeNull();
    }

    [Fact]
    public void UseDurableTaskScheduler_Worker_WithConnectionString_ShouldConfigureCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockBuilder = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=DefaultAzure;TaskHub={ValidTaskHub}";

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(connectionString);

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetService<IOptions<GrpcDurableTaskWorkerOptions>>();
        options.Should().NotBeNull();
    }

    [Fact]
    public void UseDurableTaskScheduler_Client_WithEndpointAndCredential_ShouldConfigureCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockBuilder = new Mock<IDurableTaskClientBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        var credential = new DefaultAzureCredential();

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential);

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetService<IOptions<GrpcDurableTaskClientOptions>>();
        options.Should().NotBeNull();
    }

    [Fact]
    public void UseDurableTaskScheduler_Client_WithConnectionString_ShouldConfigureCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockBuilder = new Mock<IDurableTaskClientBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=DefaultAzure;TaskHub={ValidTaskHub}";

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(connectionString);

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetService<IOptions<GrpcDurableTaskClientOptions>>();
        options.Should().NotBeNull();
    }

    [Fact]
    public void UseDurableTaskScheduler_WithOptions_ShouldApplyConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockBuilder = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        var credential = new DefaultAzureCredential();
        string workerId = "customWorker";

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(
            ValidEndpoint,
            ValidTaskHub,
            credential,
            options => options.WorkerId = workerId);

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetService<IOptions<GrpcDurableTaskWorkerOptions>>();
        options.Should().NotBeNull();
    }

    [Theory]
    [InlineData(null, ValidTaskHub)]
    [InlineData(ValidEndpoint, null)]
    public void UseDurableTaskScheduler_WithNullParameters_ShouldThrowArgumentNullException(string endpoint, string taskHub)
    {
        // Arrange
        var services = new ServiceCollection();
        var mockBuilder = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        var credential = new DefaultAzureCredential();

        // Act & Assert
        var action = () => mockBuilder.Object.UseDurableTaskScheduler(endpoint, taskHub, credential);
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UseDurableTaskScheduler_WithNullCredential_ShouldThrowArgumentException()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockBuilder = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        TokenCredential? credential = null;

        // Act & Assert
        var action = () => mockBuilder.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential!);
        action.Should().Throw<ArgumentException>()
            .WithMessage("*Required option 'Credential' was not provided*");
    }

    [Fact]
    public void UseDurableTaskScheduler_WithInvalidConnectionString_ShouldThrowArgumentException()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockBuilder = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        string invalidConnectionString = "This is not a valid connection string";

        // Act & Assert
        var action = () => mockBuilder.Object.UseDurableTaskScheduler(invalidConnectionString);
        action.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void UseDurableTaskScheduler_WithNullOrEmptyConnectionString_ShouldThrowArgumentException(string connectionString)
    {
        // Arrange
        var services = new ServiceCollection();
        var mockBuilder = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);

        // Act & Assert
        var action = () => mockBuilder.Object.UseDurableTaskScheduler(connectionString);
        action.Should().Throw<ArgumentException>();
    }
}
