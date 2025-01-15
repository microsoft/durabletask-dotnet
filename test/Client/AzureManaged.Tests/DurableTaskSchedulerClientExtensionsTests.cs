// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.DurableTask.Client.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.DurableTask.Client.AzureManaged.Tests;

public class DurableTaskSchedulerClientExtensionsTests
{
    const string ValidEndpoint = "myaccount.westus3.durabletask.io";
    const string ValidTaskHub = "testhub";

    [Fact]
    public void UseDurableTaskScheduler_WithEndpointAndCredential_ShouldConfigureCorrectly()
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskClientBuilder> mockBuilder = new Mock<IDurableTaskClientBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        DefaultAzureCredential credential = new DefaultAzureCredential();

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential);

        // Assert
        ServiceProvider provider = services.BuildServiceProvider();
        IOptions<GrpcDurableTaskClientOptions>? options = provider.GetService<IOptions<GrpcDurableTaskClientOptions>>();
        options.Should().NotBeNull();

        // Validate the configured options
        DurableTaskSchedulerClientOptions clientOptions = provider.GetRequiredService<IOptions<DurableTaskSchedulerClientOptions>>().Value;
        clientOptions.EndpointAddress.Should().Be(ValidEndpoint);
        clientOptions.TaskHubName.Should().Be(ValidTaskHub);
        clientOptions.Credential.Should().BeOfType<DefaultAzureCredential>();
    }

    [Fact]
    public void UseDurableTaskScheduler_WithConnectionString_ShouldConfigureCorrectly()
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskClientBuilder> mockBuilder = new Mock<IDurableTaskClientBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=DefaultAzure;TaskHub={ValidTaskHub}";

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(connectionString);

        // Assert
        ServiceProvider provider = services.BuildServiceProvider();
        IOptions<GrpcDurableTaskClientOptions>? options = provider.GetService<IOptions<GrpcDurableTaskClientOptions>>();
        options.Should().NotBeNull();

        // Validate the configured options
        DurableTaskSchedulerClientOptions clientOptions = provider.GetRequiredService<IOptions<DurableTaskSchedulerClientOptions>>().Value;
        clientOptions.EndpointAddress.Should().Be(ValidEndpoint);
        clientOptions.TaskHubName.Should().Be(ValidTaskHub);
        clientOptions.Credential.Should().BeOfType<DefaultAzureCredential>();
    }

    [Theory]
    [InlineData(null, "testhub")]
    [InlineData("myaccount.westus3.durabletask.io", null)]
    public void UseDurableTaskScheduler_WithNullParameters_ShouldThrowOptionsValidationException(string endpoint, string taskHub)
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskClientBuilder> mockBuilder = new Mock<IDurableTaskClientBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        DefaultAzureCredential credential = new DefaultAzureCredential();

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(endpoint, taskHub, credential);
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        var action = () => provider.GetRequiredService<IOptions<DurableTaskSchedulerClientOptions>>().Value;
        action.Should().Throw<OptionsValidationException>()
            .WithMessage(endpoint == null 
                ? "DataAnnotation validation failed for 'DurableTaskSchedulerClientOptions' members: 'EndpointAddress' with the error: 'Endpoint address is required'."
                : "DataAnnotation validation failed for 'DurableTaskSchedulerClientOptions' members: 'TaskHubName' with the error: 'Task hub name is required'.");
    }


    [Fact]
    public void UseDurableTaskScheduler_WithNullCredential_ShouldSucceed()
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskClientBuilder> mockBuilder = new Mock<IDurableTaskClientBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        TokenCredential? credential = null;

        // Act & Assert
        Action action = () => mockBuilder.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential!);
        action.Should().NotThrow();

        // Validate the configured options
        ServiceProvider provider = services.BuildServiceProvider();
        DurableTaskSchedulerClientOptions clientOptions = provider.GetRequiredService<IOptions<DurableTaskSchedulerClientOptions>>().Value;
        clientOptions.EndpointAddress.Should().Be(ValidEndpoint);
        clientOptions.TaskHubName.Should().Be(ValidTaskHub);
        clientOptions.Credential.Should().BeNull();
    }

    [Fact]
    public void UseDurableTaskScheduler_WithInvalidConnectionString_ShouldThrowArgumentException()
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskClientBuilder> mockBuilder = new Mock<IDurableTaskClientBuilder>();
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
        Mock<IDurableTaskClientBuilder> mockBuilder = new Mock<IDurableTaskClientBuilder>();
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
        Mock<IDurableTaskClientBuilder> mockBuilder = new Mock<IDurableTaskClientBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        mockBuilder.Setup(b => b.Name).Returns("CustomName");
        DefaultAzureCredential credential = new DefaultAzureCredential();

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential);

        // Assert
        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DurableTaskSchedulerClientOptions>? optionsMonitor = provider.GetService<IOptionsMonitor<DurableTaskSchedulerClientOptions>>();
        optionsMonitor.Should().NotBeNull();
        DurableTaskSchedulerClientOptions options = optionsMonitor!.Get("CustomName");
        options.Should().NotBeNull();
        options.EndpointAddress.Should().Be(ValidEndpoint); // The https:// prefix is added by CreateChannel, not in the extension method
        options.TaskHubName.Should().Be(ValidTaskHub);
        options.Credential.Should().BeOfType<DefaultAzureCredential>();
        options.ResourceId.Should().Be("https://durabletask.io");
        options.AllowInsecureCredentials.Should().BeFalse();
    }
}
