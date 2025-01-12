// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Grpc.Net.Client;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using System.ComponentModel.DataAnnotations;
using Xunit;

namespace Microsoft.DurableTask.Client.AzureManaged.Tests;

public class DurableTaskSchedulerClientExtensionsTests
{
    private const string ValidEndpoint = "myaccount.westus3.durabletask.io";
    private const string ValidTaskHub = "testhub";

    [Fact]
    public void UseDurableTaskScheduler_WithEndpointAndCredential_ShouldConfigureCorrectly()
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
    public void UseDurableTaskScheduler_WithConnectionString_ShouldConfigureCorrectly()
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

    [Theory]
    [InlineData(null, "testhub")]
    [InlineData("myaccount.westus3.durabletask.io", null)]
    public void UseDurableTaskScheduler_WithNullParameters_ShouldThrowArgumentNullException(string endpoint, string taskHub)
    {
        // Arrange
        var services = new ServiceCollection();
        var mockBuilder = new Mock<IDurableTaskClientBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        var credential = new DefaultAzureCredential();

        // Act
        var action = () => mockBuilder.Object.UseDurableTaskScheduler(endpoint, taskHub, credential);

        // Assert
        action.Should().NotThrow(); // The validation happens when building the service provider
        
        if (endpoint == null || taskHub == null)
        {
            var provider = services.BuildServiceProvider();
            var ex = Assert.Throws<OptionsValidationException>(() =>
            {
                var options = provider.GetRequiredService<IOptions<DurableTaskSchedulerOptions>>().Value;
            });
            Assert.Contains(endpoint == null ? "EndpointAddress" : "TaskHubName", ex.Message);
        }
    }

    [Fact]
    public void UseDurableTaskScheduler_WithNullCredential_ShouldSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockBuilder = new Mock<IDurableTaskClientBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        TokenCredential? credential = null;

        // Act & Assert
        var action = () => mockBuilder.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential!);
        action.Should().NotThrow();
    }

    [Fact]
    public void UseDurableTaskScheduler_WithInvalidConnectionString_ShouldThrowArgumentException()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockBuilder = new Mock<IDurableTaskClientBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        var connectionString = "This is not a valid=connection string format";

        // Act
        var action = () => mockBuilder.Object.UseDurableTaskScheduler(connectionString);

        // Assert
        action.Should().Throw<ArgumentNullException>()
            .WithMessage("Value cannot be null. (Parameter 'The connection string is missing the required 'Endpoint' property.')");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void UseDurableTaskScheduler_WithNullOrEmptyConnectionString_ShouldThrowArgumentException(string connectionString)
    {
        // Arrange
        var services = new ServiceCollection();
        var mockBuilder = new Mock<IDurableTaskClientBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);

        // Act & Assert
        var action = () => mockBuilder.Object.UseDurableTaskScheduler(connectionString);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UseDurableTaskScheduler_WithNamedOptions_ShouldConfigureCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockBuilder = new Mock<IDurableTaskClientBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        mockBuilder.Setup(b => b.Name).Returns("CustomName");
        var credential = new DefaultAzureCredential();

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential);

        // Assert
        var provider = services.BuildServiceProvider();
        var optionsMonitor = provider.GetService<IOptionsMonitor<DurableTaskSchedulerOptions>>();
        optionsMonitor.Should().NotBeNull();
        var options = optionsMonitor!.Get("CustomName");
        options.Should().NotBeNull();
        options.EndpointAddress.Should().Be(ValidEndpoint); // The https:// prefix is added by CreateChannel, not in the extension method
        options.TaskHubName.Should().Be(ValidTaskHub);
        options.Credential.Should().BeOfType<DefaultAzureCredential>();
    }

    [Fact]
    public void ConfigureGrpcChannel_ShouldConfigureClientOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddOptions<DurableTaskSchedulerOptions>()
            .Configure(options =>
            {
                options.EndpointAddress = $"https://{ValidEndpoint}";
                options.TaskHubName = ValidTaskHub;
                options.Credential = new DefaultAzureCredential();
            });

        var provider = services.BuildServiceProvider();
        var schedulerOptions = provider.GetRequiredService<IOptionsMonitor<DurableTaskSchedulerOptions>>();
        var configureGrpcChannel = new DurableTaskSchedulerClientExtensions.ConfigureGrpcChannel(schedulerOptions);

        // Act
        var clientOptions = new GrpcDurableTaskClientOptions();
        configureGrpcChannel.Configure(clientOptions);

        // Assert
        clientOptions.Channel.Should().NotBeNull();
        clientOptions.Channel.Should().BeOfType<GrpcChannel>();
    }
}  
