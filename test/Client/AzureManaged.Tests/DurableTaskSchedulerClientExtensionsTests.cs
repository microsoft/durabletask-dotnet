// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
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

    [Fact]
    public void UseDurableTaskScheduler_WithLocalhostConnectionString_ShouldConfigureCorrectly()
    {
        // Arrange
        ServiceCollection services = new();
        Mock<IDurableTaskClientBuilder> mockBuilder = new();
        mockBuilder.Setup(b => b.Services).Returns(services);
        string connectionString = $"Endpoint=http://localhost;Authentication=None;TaskHub={ValidTaskHub}";

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(connectionString);

        // Assert
        ServiceProvider provider = services.BuildServiceProvider();
        IOptions<GrpcDurableTaskClientOptions>? options = provider.GetService<IOptions<GrpcDurableTaskClientOptions>>();
        options.Should().NotBeNull();

        // Validate the configured options
        var workerOptions = provider.GetRequiredService<IOptions<DurableTaskSchedulerClientOptions>>().Value;
        workerOptions.EndpointAddress.Should().Be("http://localhost");
        workerOptions.TaskHubName.Should().Be(ValidTaskHub);
        workerOptions.Credential.Should().BeNull();
        workerOptions.ResourceId.Should().Be("https://durabletask.io");
        workerOptions.AllowInsecureCredentials.Should().BeTrue();
    }

    [Theory]
    [InlineData(null, "testhub")]
    [InlineData("myaccount.westus3.durabletask.io", null)]
    public void UseDurableTaskScheduler_WithNullParameters_ShouldThrowOptionsValidationException(string? endpoint, string? taskHub)
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskClientBuilder> mockBuilder = new Mock<IDurableTaskClientBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        DefaultAzureCredential credential = new DefaultAzureCredential();

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(endpoint!, taskHub!, credential);
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
            .WithMessage("Value cannot be null. (Parameter '*')");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void UseDurableTaskScheduler_WithNullOrEmptyConnectionString_ShouldThrowArgumentException(string? connectionString)
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskClientBuilder> mockBuilder = new Mock<IDurableTaskClientBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);

        // Act & Assert
        Action action = () => mockBuilder.Object.UseDurableTaskScheduler(connectionString!);
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

    [Fact]
    public void UseDurableTaskScheduler_WithEndpointAndCredentialAndRetryOptions_ShouldConfigureCorrectly()
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskClientBuilder> mockBuilder = new Mock<IDurableTaskClientBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        DefaultAzureCredential credential = new DefaultAzureCredential();

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential, options =>
                options.RetryOptions = new DurableTaskSchedulerClientOptions.ClientRetryOptions
                {
                    MaxRetries = 5,
                    InitialBackoffMs = 100,
                    MaxBackoffMs = 1000,
                    BackoffMultiplier = 2.0,
                    RetryableStatusCodes = new List<StatusCode> { StatusCode.Unknown }
                }
            );

        // Assert
        ServiceProvider provider = services.BuildServiceProvider();
        IOptions<GrpcDurableTaskClientOptions>? options = provider.GetService<IOptions<GrpcDurableTaskClientOptions>>();
        options.Should().NotBeNull();

        // Validate the configured options
        DurableTaskSchedulerClientOptions clientOptions = provider.GetRequiredService<IOptions<DurableTaskSchedulerClientOptions>>().Value;
        clientOptions.EndpointAddress.Should().Be(ValidEndpoint);
        clientOptions.TaskHubName.Should().Be(ValidTaskHub);
        clientOptions.Credential.Should().BeOfType<DefaultAzureCredential>();
        clientOptions.RetryOptions.Should().NotBeNull();
        // The assert not null doesn't clear the syntax warning about null checks.
        if (clientOptions.RetryOptions != null)
        {
            clientOptions.RetryOptions.MaxRetries.Should().Be(5);
            clientOptions.RetryOptions.InitialBackoffMs.Should().Be(100);
            clientOptions.RetryOptions.MaxBackoffMs.Should().Be(1000);
            clientOptions.RetryOptions.BackoffMultiplier.Should().Be(2.0);
            clientOptions.RetryOptions.RetryableStatusCodes.Should().Contain(StatusCode.Unknown);
        }
    }

    [Fact]
    public void UseDurableTaskScheduler_WithConnectionStringAndRetryOptions_ShouldConfigureCorrectly()
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskClientBuilder> mockBuilder = new Mock<IDurableTaskClientBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=DefaultAzure;TaskHub={ValidTaskHub}";

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(connectionString, options =>
                options.RetryOptions = new DurableTaskSchedulerClientOptions.ClientRetryOptions
                {
                    MaxRetries = 5,
                    InitialBackoffMs = 100,
                    MaxBackoffMs = 1000,
                    BackoffMultiplier = 2.0,
                    RetryableStatusCodes = new List<StatusCode> { StatusCode.Unknown }
                }
            );

        // Assert
        ServiceProvider provider = services.BuildServiceProvider();
        IOptions<GrpcDurableTaskClientOptions>? options = provider.GetService<IOptions<GrpcDurableTaskClientOptions>>();
        options.Should().NotBeNull();

        // Validate the configured options
        DurableTaskSchedulerClientOptions clientOptions = provider.GetRequiredService<IOptions<DurableTaskSchedulerClientOptions>>().Value;
        clientOptions.EndpointAddress.Should().Be(ValidEndpoint);
        clientOptions.TaskHubName.Should().Be(ValidTaskHub);
        clientOptions.Credential.Should().BeOfType<DefaultAzureCredential>();
        clientOptions.RetryOptions.Should().NotBeNull();
        // The assert not null doesn't clear the syntax warning about null checks.
        if (clientOptions.RetryOptions != null)
        {
            clientOptions.RetryOptions.MaxRetries.Should().Be(5);
            clientOptions.RetryOptions.InitialBackoffMs.Should().Be(100);
            clientOptions.RetryOptions.MaxBackoffMs.Should().Be(1000);
            clientOptions.RetryOptions.BackoffMultiplier.Should().Be(2.0);
            clientOptions.RetryOptions.RetryableStatusCodes.Should().Contain(StatusCode.Unknown);
        }
    }

    [Fact]
    public void UseDurableTaskScheduler_SameConfiguration_ReusesSameChannel()
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskClientBuilder> mockBuilder = new Mock<IDurableTaskClientBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        DefaultAzureCredential credential = new DefaultAzureCredential();

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential);
        ServiceProvider provider = services.BuildServiceProvider();

        // Resolve options multiple times to trigger channel configuration
        IOptionsMonitor<GrpcDurableTaskClientOptions> optionsMonitor = provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskClientOptions>>();
        GrpcDurableTaskClientOptions options1 = optionsMonitor.Get(Options.DefaultName);
        GrpcDurableTaskClientOptions options2 = optionsMonitor.Get(Options.DefaultName);

        // Assert
        options1.Channel.Should().NotBeNull();
        options2.Channel.Should().NotBeNull();
        options1.Channel.Should().BeSameAs(options2.Channel, "same configuration should reuse the same channel");
    }

    [Fact]
    public void UseDurableTaskScheduler_DifferentNamedOptions_UsesSeparateChannels()
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskClientBuilder> mockBuilder1 = new Mock<IDurableTaskClientBuilder>();
        Mock<IDurableTaskClientBuilder> mockBuilder2 = new Mock<IDurableTaskClientBuilder>();
        mockBuilder1.Setup(b => b.Services).Returns(services);
        mockBuilder1.Setup(b => b.Name).Returns("client1");
        mockBuilder2.Setup(b => b.Services).Returns(services);
        mockBuilder2.Setup(b => b.Name).Returns("client2");
        DefaultAzureCredential credential = new DefaultAzureCredential();

        // Act - configure two different named clients with different endpoints
        mockBuilder1.Object.UseDurableTaskScheduler("endpoint1.westus3.durabletask.io", ValidTaskHub, credential);
        mockBuilder2.Object.UseDurableTaskScheduler("endpoint2.westus3.durabletask.io", ValidTaskHub, credential);
        ServiceProvider provider = services.BuildServiceProvider();

        // Resolve options for both named clients
        IOptionsMonitor<GrpcDurableTaskClientOptions> optionsMonitor = provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskClientOptions>>();
        GrpcDurableTaskClientOptions options1 = optionsMonitor.Get("client1");
        GrpcDurableTaskClientOptions options2 = optionsMonitor.Get("client2");

        // Assert
        options1.Channel.Should().NotBeNull();
        options2.Channel.Should().NotBeNull();
        options1.Channel.Should().NotBeSameAs(options2.Channel, "different configurations should use different channels");
    }

    [Fact]
    public async Task UseDurableTaskScheduler_ServiceProviderDispose_DisposesChannels()
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskClientBuilder> mockBuilder = new Mock<IDurableTaskClientBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        DefaultAzureCredential credential = new DefaultAzureCredential();

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential);
        ServiceProvider provider = services.BuildServiceProvider();

        // Resolve options to trigger channel creation
        IOptionsMonitor<GrpcDurableTaskClientOptions> optionsMonitor = provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskClientOptions>>();
        GrpcDurableTaskClientOptions options = optionsMonitor.Get(Options.DefaultName);
        options.Channel.Should().NotBeNull();
        GrpcChannel channel = options.Channel!;

        // Dispose the service provider - this should dispose the ConfigureGrpcChannel which disposes channels
        await provider.DisposeAsync();

        // Assert - verify the channel was disposed by checking it throws ObjectDisposedException
        Action action = () => channel.CreateCallInvoker();
        action.Should().Throw<ObjectDisposedException>("channel should be disposed after provider disposal");

        // Also verify that creating a new provider and getting options still works
        ServiceCollection services2 = new ServiceCollection();
        Mock<IDurableTaskClientBuilder> mockBuilder2 = new Mock<IDurableTaskClientBuilder>();
        mockBuilder2.Setup(b => b.Services).Returns(services2);
        mockBuilder2.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential);
        await using ServiceProvider provider2 = services2.BuildServiceProvider();

        IOptionsMonitor<GrpcDurableTaskClientOptions> newOptionsMonitor = provider2.GetRequiredService<IOptionsMonitor<GrpcDurableTaskClientOptions>>();
        GrpcDurableTaskClientOptions newOptions = newOptionsMonitor.Get(Options.DefaultName);
        newOptions.Channel.Should().NotBeNull();
        newOptions.Channel.Should().NotBeSameAs(channel, "new provider should create a new channel");
    }

    [Fact]
    public async Task UseDurableTaskScheduler_ConfigureAfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskClientBuilder> mockBuilder = new Mock<IDurableTaskClientBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        DefaultAzureCredential credential = new DefaultAzureCredential();

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential);
        ServiceProvider provider = services.BuildServiceProvider();

        // Resolve options monitor before disposal
        IOptionsMonitor<GrpcDurableTaskClientOptions> optionsMonitor = provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskClientOptions>>();

        // Dispose the service provider
        await provider.DisposeAsync();

        // Assert - attempting to get options after disposal should throw
        Action action = () => optionsMonitor.Get(Options.DefaultName);
        action.Should().Throw<ObjectDisposedException>("configuring options after disposal should throw");
    }
}
