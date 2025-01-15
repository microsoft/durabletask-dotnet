// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.DurableTask.Worker.AzureManaged.Tests;

public class DurableTaskSchedulerWorkerExtensionsTests
{
    const string ValidEndpoint = "myaccount.westus3.durabletask.io";
    const string ValidTaskHub = "testhub";

    [Fact]
    public void UseDurableTaskScheduler_WithEndpointAndCredential_ShouldConfigureCorrectly()
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        DefaultAzureCredential credential = new DefaultAzureCredential();

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential);

        // Assert
        ServiceProvider provider = services.BuildServiceProvider();
        IOptions<GrpcDurableTaskWorkerOptions>? options = provider.GetService<IOptions<GrpcDurableTaskWorkerOptions>>();
        options.Should().NotBeNull();

        // Validate the configured options
        var workerOptions = provider.GetRequiredService<IOptions<DurableTaskSchedulerWorkerOptions>>().Value;
        workerOptions.EndpointAddress.Should().Be(ValidEndpoint);
        workerOptions.TaskHubName.Should().Be(ValidTaskHub);
        workerOptions.Credential.Should().BeOfType<DefaultAzureCredential>();
        workerOptions.ResourceId.Should().Be("https://durabletask.io");
        workerOptions.AllowInsecureCredentials.Should().BeFalse();
    }

    [Fact]
    public void UseDurableTaskScheduler_WithConnectionString_ShouldConfigureCorrectly()
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=DefaultAzure;TaskHub={ValidTaskHub}";

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(connectionString);

        // Assert
        ServiceProvider provider = services.BuildServiceProvider();
        IOptions<GrpcDurableTaskWorkerOptions>? options = provider.GetService<IOptions<GrpcDurableTaskWorkerOptions>>();
        options.Should().NotBeNull();

        // Validate the configured options
        var workerOptions = provider.GetRequiredService<IOptions<DurableTaskSchedulerWorkerOptions>>().Value;
        workerOptions.EndpointAddress.Should().Be(ValidEndpoint);
        workerOptions.TaskHubName.Should().Be(ValidTaskHub);
        workerOptions.Credential.Should().BeOfType<DefaultAzureCredential>();
        workerOptions.ResourceId.Should().Be("https://durabletask.io");
        workerOptions.AllowInsecureCredentials.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "testhub")]
    [InlineData("myaccount.westus3.durabletask.io", null)]
    public void UseDurableTaskScheduler_WithNullParameters_ShouldThrowArgumentNullException(string endpoint, string taskHub)
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        DefaultAzureCredential credential = new DefaultAzureCredential();

        // Act
        Action action = () => mockBuilder.Object.UseDurableTaskScheduler(endpoint, taskHub, credential);

        // Assert
        action.Should().NotThrow(); // The validation happens when building the service provider

        if (endpoint == null || taskHub == null)
        {
            ServiceProvider provider = services.BuildServiceProvider();
            OptionsValidationException ex = Assert.Throws<OptionsValidationException>(() =>
            {
                DurableTaskSchedulerWorkerOptions options = provider.GetRequiredService<IOptions<DurableTaskSchedulerWorkerOptions>>().Value;
            });
            Assert.Contains(endpoint == null ? "EndpointAddress" : "TaskHubName", ex.Message);
        }
    }

    [Fact]
    public void UseDurableTaskScheduler_WithNullCredential_ShouldSucceed()
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        TokenCredential? credential = null;

        // Act & Assert
        Action action = () => mockBuilder.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential!);
        action.Should().NotThrow();

        // Validate the configured options
        ServiceProvider provider = services.BuildServiceProvider();
        var workerOptions = provider.GetRequiredService<IOptions<DurableTaskSchedulerWorkerOptions>>().Value;
        workerOptions.EndpointAddress.Should().Be(ValidEndpoint);
        workerOptions.TaskHubName.Should().Be(ValidTaskHub);
        workerOptions.Credential.Should().BeNull();
        workerOptions.ResourceId.Should().Be("https://durabletask.io");
        workerOptions.AllowInsecureCredentials.Should().BeFalse();
    }

    [Fact]
    public void UseDurableTaskScheduler_WithInvalidConnectionString_ShouldThrowArgumentException()
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        string connectionString = "This is not a valid=connection string format";

        // Act
        Action action = () => mockBuilder.Object.UseDurableTaskScheduler(connectionString);

        // Assert
        action.Should().Throw<ArgumentNullException>()
            .WithMessage("Value cannot be null. (Parameter 'Endpoint')");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void UseDurableTaskScheduler_WithNullOrEmptyConnectionString_ShouldThrowArgumentException(string connectionString)
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);

        // Act & Assert
        Action action = () => mockBuilder.Object.UseDurableTaskScheduler(connectionString);
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UseDurableTaskScheduler_WithNamedOptions_ShouldConfigureCorrectly()
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        mockBuilder.Setup(b => b.Name).Returns("CustomName");
        DefaultAzureCredential credential = new DefaultAzureCredential();

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential);

        // Assert
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskSchedulerWorkerOptions>? optionsMonitor = provider.GetService<IOptionsMonitor<DurableTaskSchedulerWorkerOptions>>();
        optionsMonitor.Should().NotBeNull();
        DurableTaskSchedulerWorkerOptions options = optionsMonitor!.Get("CustomName");
        options.Should().NotBeNull();
        options.EndpointAddress.Should().Be(ValidEndpoint); // The https:// prefix is added by CreateChannel, not in the extension method
        options.TaskHubName.Should().Be(ValidTaskHub);
        options.Credential.Should().BeOfType<DefaultAzureCredential>();
        options.ResourceId.Should().Be("https://durabletask.io");
        options.AllowInsecureCredentials.Should().BeFalse();
    }
}
