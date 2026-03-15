// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Grpc.Net.Client;
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
    public async Task UseDurableTaskScheduler_WithEndpointAndCredential_ShouldConfigureCorrectly()
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        DefaultAzureCredential credential = new DefaultAzureCredential();

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential);

        // Assert
        await using ServiceProvider provider = services.BuildServiceProvider();
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
    public async Task UseDurableTaskScheduler_WithConnectionString_ShouldConfigureCorrectly()
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        string connectionString = $"Endpoint={ValidEndpoint};Authentication=DefaultAzure;TaskHub={ValidTaskHub}";

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(connectionString);

        // Assert
        await using ServiceProvider provider = services.BuildServiceProvider();
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
    public async Task UseDurableTaskScheduler_WithLocalhostConnectionString_ShouldConfigureCorrectly()
    {
        // Arrange
        ServiceCollection services = new();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new();
        mockBuilder.Setup(b => b.Services).Returns(services);
        string connectionString = $"Endpoint=http://localhost;Authentication=None;TaskHub={ValidTaskHub}";

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(connectionString);

        // Assert
        await using ServiceProvider provider = services.BuildServiceProvider();
        IOptions<GrpcDurableTaskWorkerOptions>? options = provider.GetService<IOptions<GrpcDurableTaskWorkerOptions>>();
        options.Should().NotBeNull();

        // Validate the configured options
        var workerOptions = provider.GetRequiredService<IOptions<DurableTaskSchedulerWorkerOptions>>().Value;
        workerOptions.EndpointAddress.Should().Be("http://localhost");
        workerOptions.TaskHubName.Should().Be(ValidTaskHub);
        workerOptions.Credential.Should().BeNull();
        workerOptions.ResourceId.Should().Be("https://durabletask.io");
        workerOptions.AllowInsecureCredentials.Should().BeTrue();
    }

    [Theory]
    [InlineData(null, "testhub")]
    [InlineData("myaccount.westus3.durabletask.io", null)]
    public async Task UseDurableTaskScheduler_WithNullParameters_ShouldThrowOptionsValidationException(string? endpoint, string? taskHub)
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        DefaultAzureCredential credential = new DefaultAzureCredential();

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(endpoint!, taskHub!, credential);
        await using ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        var action = () => provider.GetRequiredService<IOptions<DurableTaskSchedulerWorkerOptions>>().Value;
        action.Should().Throw<OptionsValidationException>()
            .WithMessage(endpoint == null
                ? "DataAnnotation validation failed for 'DurableTaskSchedulerWorkerOptions' members: 'EndpointAddress' with the error: 'Endpoint address is required'."
                : "DataAnnotation validation failed for 'DurableTaskSchedulerWorkerOptions' members: 'TaskHubName' with the error: 'Task hub name is required'.");
    }

    [Fact]
    public async Task UseDurableTaskScheduler_WithNullCredential_ShouldSucceed()
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
        await using ServiceProvider provider = services.BuildServiceProvider();
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
            .WithMessage("Value cannot be null. (Parameter '*')");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void UseDurableTaskScheduler_WithNullOrEmptyConnectionString_ShouldThrowArgumentException(string? connectionString)
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);

        // Act & Assert
        Action action = () => mockBuilder.Object.UseDurableTaskScheduler(connectionString!);
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task UseDurableTaskScheduler_WithNamedOptions_ShouldConfigureCorrectly()
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
        await using ServiceProvider provider = services.BuildServiceProvider();
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

    [Fact]
    public async Task UseDurableTaskScheduler_SameConfiguration_ReusesSameChannel()
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        DefaultAzureCredential credential = new DefaultAzureCredential();

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential);
        await using ServiceProvider provider = services.BuildServiceProvider();

        // Resolve options multiple times to trigger channel configuration via new options instances
        IOptionsFactory<GrpcDurableTaskWorkerOptions> optionsFactory = provider.GetRequiredService<IOptionsFactory<GrpcDurableTaskWorkerOptions>>();
        GrpcDurableTaskWorkerOptions options1 = optionsFactory.Create(Options.DefaultName);
        GrpcDurableTaskWorkerOptions options2 = optionsFactory.Create(Options.DefaultName);

        // Assert
        options1.Channel.Should().NotBeNull();
        options2.Channel.Should().NotBeNull();
        options1.Channel.Should().BeSameAs(options2.Channel, "same configuration should reuse the same channel");
    }

    [Fact]
    public async Task UseDurableTaskScheduler_DifferentNamedOptions_UsesSeparateChannels()
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskWorkerBuilder> mockBuilder1 = new Mock<IDurableTaskWorkerBuilder>();
        Mock<IDurableTaskWorkerBuilder> mockBuilder2 = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder1.Setup(b => b.Services).Returns(services);
        mockBuilder1.Setup(b => b.Name).Returns("worker1");
        mockBuilder2.Setup(b => b.Services).Returns(services);
        mockBuilder2.Setup(b => b.Name).Returns("worker2");
        DefaultAzureCredential credential = new DefaultAzureCredential();

        // Act - configure two different named workers with the same endpoint and task hub
        mockBuilder1.Object.UseDurableTaskScheduler("endpoint.westus3.durabletask.io", ValidTaskHub, credential);
        mockBuilder2.Object.UseDurableTaskScheduler("endpoint.westus3.durabletask.io", ValidTaskHub, credential);
        await using ServiceProvider provider = services.BuildServiceProvider();

        // Resolve options for both named workers
        IOptionsMonitor<GrpcDurableTaskWorkerOptions> optionsMonitor = provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskWorkerOptions>>();
        GrpcDurableTaskWorkerOptions options1 = optionsMonitor.Get("worker1");
        GrpcDurableTaskWorkerOptions options2 = optionsMonitor.Get("worker2");

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
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        DefaultAzureCredential credential = new DefaultAzureCredential();

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential);
        
        GrpcChannel channel;
        await using (ServiceProvider provider = services.BuildServiceProvider())
        {
            // Resolve options to trigger channel creation
            IOptionsMonitor<GrpcDurableTaskWorkerOptions> optionsMonitor = provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskWorkerOptions>>();
            GrpcDurableTaskWorkerOptions options = optionsMonitor.Get(Options.DefaultName);
            options.Channel.Should().NotBeNull();
            channel = options.Channel!;
        }

        // Assert - verify the channel was disposed by checking it throws ObjectDisposedException
        Action action = () => channel.CreateCallInvoker();
        action.Should().Throw<ObjectDisposedException>("channel should be disposed after provider disposal");

        // Also verify that creating a new provider and getting options still works
        ServiceCollection services2 = new ServiceCollection();
        Mock<IDurableTaskWorkerBuilder> mockBuilder2 = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder2.Setup(b => b.Services).Returns(services2);
        mockBuilder2.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential);
        await using ServiceProvider provider2 = services2.BuildServiceProvider();

        IOptionsMonitor<GrpcDurableTaskWorkerOptions> newOptionsMonitor = provider2.GetRequiredService<IOptionsMonitor<GrpcDurableTaskWorkerOptions>>();
        GrpcDurableTaskWorkerOptions newOptions = newOptionsMonitor.Get(Options.DefaultName);
        newOptions.Channel.Should().NotBeNull();
        newOptions.Channel.Should().NotBeSameAs(channel, "new provider should create a new channel");
    }

    [Fact]
    public async Task UseDurableTaskScheduler_ConfigureAfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskWorkerBuilder> mockBuilder = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder.Setup(b => b.Services).Returns(services);
        DefaultAzureCredential credential = new DefaultAzureCredential();

        // Act
        mockBuilder.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential);
        
        IOptionsMonitor<GrpcDurableTaskWorkerOptions> optionsMonitor;
        await using (ServiceProvider provider = services.BuildServiceProvider())
        {
            // Resolve options monitor before disposal
            optionsMonitor = provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskWorkerOptions>>();
        }

        // Assert - attempting to get options after disposal should throw
        Action action = () => optionsMonitor.Get(Options.DefaultName);
        action.Should().Throw<ObjectDisposedException>("configuring options after disposal should throw");
    }

    [Fact]
    public async Task UseDurableTaskScheduler_DifferentResourceId_UsesSeparateChannels()
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskWorkerBuilder> mockBuilder1 = new Mock<IDurableTaskWorkerBuilder>();
        Mock<IDurableTaskWorkerBuilder> mockBuilder2 = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder1.Setup(b => b.Services).Returns(services);
        mockBuilder1.Setup(b => b.Name).Returns("worker1");
        mockBuilder2.Setup(b => b.Services).Returns(services);
        mockBuilder2.Setup(b => b.Name).Returns("worker2");
        DefaultAzureCredential credential = new DefaultAzureCredential();

        // Act - configure two workers with the same endpoint/taskhub but different ResourceId
        mockBuilder1.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential, options => 
        {
            options.ResourceId = "https://durabletask.io";
        });
        mockBuilder2.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential, options => 
        {
            options.ResourceId = "https://custom.durabletask.io";
        });
        await using ServiceProvider provider = services.BuildServiceProvider();

        // Resolve options for both named workers
        IOptionsMonitor<GrpcDurableTaskWorkerOptions> optionsMonitor = provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskWorkerOptions>>();
        GrpcDurableTaskWorkerOptions options1 = optionsMonitor.Get("worker1");
        GrpcDurableTaskWorkerOptions options2 = optionsMonitor.Get("worker2");

        // Assert
        options1.Channel.Should().NotBeNull();
        options2.Channel.Should().NotBeNull();
        options1.Channel.Should().NotBeSameAs(options2.Channel, "different ResourceId should use different channels");
    }

    [Fact]
    public async Task UseDurableTaskScheduler_DifferentCredentialType_UsesSeparateChannels()
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskWorkerBuilder> mockBuilder1 = new Mock<IDurableTaskWorkerBuilder>();
        Mock<IDurableTaskWorkerBuilder> mockBuilder2 = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder1.Setup(b => b.Services).Returns(services);
        mockBuilder1.Setup(b => b.Name).Returns("worker1");
        mockBuilder2.Setup(b => b.Services).Returns(services);
        mockBuilder2.Setup(b => b.Name).Returns("worker2");

        // Act - configure two workers with the same endpoint/taskhub but different credential types
        mockBuilder1.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, new DefaultAzureCredential());
        mockBuilder2.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, new AzureCliCredential());
        await using ServiceProvider provider = services.BuildServiceProvider();

        // Resolve options for both named workers
        IOptionsMonitor<GrpcDurableTaskWorkerOptions> optionsMonitor = provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskWorkerOptions>>();
        GrpcDurableTaskWorkerOptions options1 = optionsMonitor.Get("worker1");
        GrpcDurableTaskWorkerOptions options2 = optionsMonitor.Get("worker2");

        // Assert
        options1.Channel.Should().NotBeNull();
        options2.Channel.Should().NotBeNull();
        options1.Channel.Should().NotBeSameAs(options2.Channel, "different credential type should use different channels");
    }

    [Fact]
    public async Task UseDurableTaskScheduler_DifferentAllowInsecureCredentials_UsesSeparateChannels()
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskWorkerBuilder> mockBuilder1 = new Mock<IDurableTaskWorkerBuilder>();
        Mock<IDurableTaskWorkerBuilder> mockBuilder2 = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder1.Setup(b => b.Services).Returns(services);
        mockBuilder1.Setup(b => b.Name).Returns("worker1");
        mockBuilder2.Setup(b => b.Services).Returns(services);
        mockBuilder2.Setup(b => b.Name).Returns("worker2");
        DefaultAzureCredential credential = new DefaultAzureCredential();

        // Act - configure two workers with the same endpoint/taskhub but different AllowInsecureCredentials
        mockBuilder1.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential, options => 
        {
            options.AllowInsecureCredentials = false;
        });
        mockBuilder2.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential, options => 
        {
            options.AllowInsecureCredentials = true;
        });
        await using ServiceProvider provider = services.BuildServiceProvider();

        // Resolve options for both named workers
        IOptionsMonitor<GrpcDurableTaskWorkerOptions> optionsMonitor = provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskWorkerOptions>>();
        GrpcDurableTaskWorkerOptions options1 = optionsMonitor.Get("worker1");
        GrpcDurableTaskWorkerOptions options2 = optionsMonitor.Get("worker2");

        // Assert
        options1.Channel.Should().NotBeNull();
        options2.Channel.Should().NotBeNull();
        options1.Channel.Should().NotBeSameAs(options2.Channel, "different AllowInsecureCredentials should use different channels");
    }

    [Fact]
    public async Task UseDurableTaskScheduler_DifferentWorkerId_UsesSeparateChannels()
    {
        // Arrange
        ServiceCollection services = new ServiceCollection();
        Mock<IDurableTaskWorkerBuilder> mockBuilder1 = new Mock<IDurableTaskWorkerBuilder>();
        Mock<IDurableTaskWorkerBuilder> mockBuilder2 = new Mock<IDurableTaskWorkerBuilder>();
        mockBuilder1.Setup(b => b.Services).Returns(services);
        mockBuilder1.Setup(b => b.Name).Returns("worker1");
        mockBuilder2.Setup(b => b.Services).Returns(services);
        mockBuilder2.Setup(b => b.Name).Returns("worker2");
        DefaultAzureCredential credential = new DefaultAzureCredential();

        // Act - configure two workers with the same endpoint/taskhub but different WorkerId
        mockBuilder1.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential, options => 
        {
            options.WorkerId = "worker-id-1";
        });
        mockBuilder2.Object.UseDurableTaskScheduler(ValidEndpoint, ValidTaskHub, credential, options => 
        {
            options.WorkerId = "worker-id-2";
        });
        await using ServiceProvider provider = services.BuildServiceProvider();

        // Resolve options for both named workers
        IOptionsMonitor<GrpcDurableTaskWorkerOptions> optionsMonitor = provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskWorkerOptions>>();
        GrpcDurableTaskWorkerOptions options1 = optionsMonitor.Get("worker1");
        GrpcDurableTaskWorkerOptions options2 = optionsMonitor.Get("worker2");

        // Assert
        options1.Channel.Should().NotBeNull();
        options2.Channel.Should().NotBeNull();
        options1.Channel.Should().NotBeSameAs(options2.Channel, "different WorkerId should use different channels");
    }
}

