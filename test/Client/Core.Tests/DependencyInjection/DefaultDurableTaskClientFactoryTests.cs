// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Client.Tests;

public class DefaultDurableTaskClientFactoryTests
{
    #region CreateClient Without Configuration Tests

    [Fact]
    public void CreateClient_NoConfiguration_ThrowsInvalidOperationException()
    {
        // Arrange
        ServiceCollection services = new();
        ServiceProvider provider = services.BuildServiceProvider();
        ILoggerFactory loggerFactory = NullLoggerFactory.Instance;

        DefaultDurableTaskClientFactory factory = new(provider, loggerFactory, configuration: null);

        // Act
        Action act = () => factory.CreateClient();

        // Assert
        act.Should().ThrowExactly<InvalidOperationException>()
            .WithMessage("*factory has not been configured*");
    }

    [Fact]
    public void CreateClient_WithGenericOverload_NoConfiguration_ThrowsInvalidOperationException()
    {
        // Arrange
        ServiceCollection services = new();
        ServiceProvider provider = services.BuildServiceProvider();
        ILoggerFactory loggerFactory = NullLoggerFactory.Instance;

        DefaultDurableTaskClientFactory factory = new(provider, loggerFactory, configuration: null);

        // Act
        Action act = () => factory.CreateClient<TestClientOptions>(null, opt => { });

        // Assert
        act.Should().ThrowExactly<InvalidOperationException>()
            .WithMessage("*factory has not been configured*");
    }

    #endregion

    #region CreateClient Basic Tests

    [Fact]
    public void CreateClient_WithConfiguration_ReturnsClient()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddOptions<TestClientOptions>().Configure(opt => opt.TestProperty = "test-value");
        ServiceProvider provider = services.BuildServiceProvider();
        ILoggerFactory loggerFactory = NullLoggerFactory.Instance;

        DefaultDurableTaskClientFactory.ClientFactoryConfiguration config = new()
        {
            ClientType = typeof(TestDurableTaskClient),
            OptionsType = typeof(TestClientOptions),
        };

        DefaultDurableTaskClientFactory factory = new(provider, loggerFactory, config);

        // Act
        DurableTaskClient client = factory.CreateClient();

        // Assert
        client.Should().NotBeNull();
        client.Should().BeOfType<TestDurableTaskClient>();
        client.Name.Should().Be(Options.DefaultName);
    }

    [Fact]
    public void CreateClient_WithNullName_UsesDefaultName()
    {
        // Arrange
        DefaultDurableTaskClientFactory.ClientFactoryConfiguration config = new()
        {
            ClientType = typeof(TestDurableTaskClient),
            OptionsType = typeof(TestClientOptions),
        };

        ServiceCollection services = new();
        services.AddOptions<TestClientOptions>();
        ServiceProvider provider = services.BuildServiceProvider();

        DefaultDurableTaskClientFactory factory = new(provider, NullLoggerFactory.Instance, config);

        // Act
        DurableTaskClient client = factory.CreateClient(null);

        // Assert
        client.Name.Should().Be(Options.DefaultName);
    }

    [Fact]
    public void CreateClient_WithEmptyName_UsesEmptyName()
    {
        // Arrange
        DefaultDurableTaskClientFactory.ClientFactoryConfiguration config = new()
        {
            ClientType = typeof(TestDurableTaskClient),
            OptionsType = typeof(TestClientOptions),
        };

        ServiceCollection services = new();
        services.AddOptions<TestClientOptions>(string.Empty);
        ServiceProvider provider = services.BuildServiceProvider();

        DefaultDurableTaskClientFactory factory = new(provider, NullLoggerFactory.Instance, config);

        // Act
        DurableTaskClient client = factory.CreateClient(string.Empty);

        // Assert
        client.Name.Should().Be(string.Empty);
    }

    #endregion

    #region Named Client Tests

    [Fact]
    public void CreateClient_WithName_ReturnsClientWithName()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddOptions<TestClientOptions>("my-client").Configure(opt => opt.TestProperty = "named-value");
        ServiceProvider provider = services.BuildServiceProvider();
        ILoggerFactory loggerFactory = NullLoggerFactory.Instance;

        DefaultDurableTaskClientFactory.ClientFactoryConfiguration config = new()
        {
            ClientType = typeof(TestDurableTaskClient),
            OptionsType = typeof(TestClientOptions),
        };

        DefaultDurableTaskClientFactory factory = new(provider, loggerFactory, config);

        // Act
        DurableTaskClient client = factory.CreateClient("my-client");

        // Assert
        client.Should().NotBeNull();
        client.Should().BeOfType<TestDurableTaskClient>();
        client.Name.Should().Be("my-client");
    }

    [Theory]
    [InlineData("taskHub1")]
    [InlineData("taskHub2")]
    [InlineData("my-custom-hub")]
    [InlineData("UPPERCASE_HUB")]
    public void CreateClient_WithVariousNames_ReturnsClientWithCorrectName(string clientName)
    {
        // Arrange
        DefaultDurableTaskClientFactory.ClientFactoryConfiguration config = new()
        {
            ClientType = typeof(TestDurableTaskClient),
            OptionsType = typeof(TestClientOptions),
        };

        ServiceCollection services = new();
        services.AddOptions<TestClientOptions>(clientName);
        ServiceProvider provider = services.BuildServiceProvider();

        DefaultDurableTaskClientFactory factory = new(provider, NullLoggerFactory.Instance, config);

        // Act
        DurableTaskClient client = factory.CreateClient(clientName);

        // Assert
        client.Name.Should().Be(clientName);
    }

    [Fact]
    public void CreateClient_MultipleNamedClients_CreatesDistinctClients()
    {
        // Arrange
        DefaultDurableTaskClientFactory.ClientFactoryConfiguration config = new()
        {
            ClientType = typeof(TestDurableTaskClient),
            OptionsType = typeof(TestClientOptions),
        };

        ServiceCollection services = new();
        services.AddOptions<TestClientOptions>("hub1").Configure(opt => opt.TestProperty = "value1");
        services.AddOptions<TestClientOptions>("hub2").Configure(opt => opt.TestProperty = "value2");
        ServiceProvider provider = services.BuildServiceProvider();

        DefaultDurableTaskClientFactory factory = new(provider, NullLoggerFactory.Instance, config);

        // Act
        DurableTaskClient client1 = factory.CreateClient("hub1");
        DurableTaskClient client2 = factory.CreateClient("hub2");

        // Assert
        client1.Should().NotBeSameAs(client2);
        client1.Name.Should().Be("hub1");
        client2.Name.Should().Be("hub2");

        TestDurableTaskClient testClient1 = (TestDurableTaskClient)client1;
        TestDurableTaskClient testClient2 = (TestDurableTaskClient)client2;
        testClient1.Options.TestProperty.Should().Be("value1");
        testClient2.Options.TestProperty.Should().Be("value2");
    }

    #endregion

    #region Custom Options Tests

    [Fact]
    public void CreateClient_WithCustomOptions_AppliesConfiguration()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddOptions<TestClientOptions>().Configure(opt => opt.TestProperty = "original-value");
        ServiceProvider provider = services.BuildServiceProvider();
        ILoggerFactory loggerFactory = NullLoggerFactory.Instance;

        DefaultDurableTaskClientFactory.ClientFactoryConfiguration config = new()
        {
            ClientType = typeof(TestDurableTaskClient),
            OptionsType = typeof(TestClientOptions),
        };

        DefaultDurableTaskClientFactory factory = new(provider, loggerFactory, config);

        // Act
        DurableTaskClient client = factory.CreateClient<TestClientOptions>(
            null,
            opt => opt.TestProperty = "custom-value");

        // Assert
        client.Should().NotBeNull();
        TestDurableTaskClient testClient = client.Should().BeOfType<TestDurableTaskClient>().Subject;
        testClient.Options.TestProperty.Should().Be("custom-value");
    }

    [Fact]
    public void CreateClient_WithCustomOptions_OverridesBaseOptions()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddOptions<TestClientOptions>("my-hub").Configure(opt =>
        {
            opt.TestProperty = "base-value";
            opt.EnableEntitySupport = false;
        });
        ServiceProvider provider = services.BuildServiceProvider();

        DefaultDurableTaskClientFactory.ClientFactoryConfiguration config = new()
        {
            ClientType = typeof(TestDurableTaskClient),
            OptionsType = typeof(TestClientOptions),
        };

        DefaultDurableTaskClientFactory factory = new(provider, NullLoggerFactory.Instance, config);

        // Act
        DurableTaskClient client = factory.CreateClient<TestClientOptions>(
            "my-hub",
            opt =>
            {
                opt.TestProperty = "overridden-value";
                opt.EnableEntitySupport = true;
            });

        // Assert
        TestDurableTaskClient testClient = (TestDurableTaskClient)client;
        testClient.Options.TestProperty.Should().Be("overridden-value");
        testClient.Options.EnableEntitySupport.Should().BeTrue();
    }

    [Fact]
    public void CreateClient_WithCustomOptions_NullConfigureAction_ThrowsArgumentNullException()
    {
        // Arrange
        DefaultDurableTaskClientFactory.ClientFactoryConfiguration config = new()
        {
            ClientType = typeof(TestDurableTaskClient),
            OptionsType = typeof(TestClientOptions),
        };

        ServiceCollection services = new();
        services.AddOptions<TestClientOptions>();
        ServiceProvider provider = services.BuildServiceProvider();

        DefaultDurableTaskClientFactory factory = new(provider, NullLoggerFactory.Instance, config);

        // Act
        Action act = () => factory.CreateClient<TestClientOptions>(null, null!);

        // Assert
        act.Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void CreateClient_WithCustomOptions_PreservesUnmodifiedProperties()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddOptions<TestClientOptions>().Configure(opt =>
        {
            opt.TestProperty = "original";
            opt.EnableEntitySupport = true;
            opt.DefaultVersion = "1.0";
        });
        ServiceProvider provider = services.BuildServiceProvider();

        DefaultDurableTaskClientFactory.ClientFactoryConfiguration config = new()
        {
            ClientType = typeof(TestDurableTaskClient),
            OptionsType = typeof(TestClientOptions),
        };

        DefaultDurableTaskClientFactory factory = new(provider, NullLoggerFactory.Instance, config);

        // Act - only modify TestProperty
        DurableTaskClient client = factory.CreateClient<TestClientOptions>(
            null,
            opt => opt.TestProperty = "modified");

        // Assert
        TestDurableTaskClient testClient = (TestDurableTaskClient)client;
        testClient.Options.TestProperty.Should().Be("modified");
        testClient.Options.EnableEntitySupport.Should().BeTrue(); // Preserved
        testClient.Options.DefaultVersion.Should().Be("1.0"); // Preserved
    }

    #endregion

    #region Service Collection Integration Tests

    [Fact]
    public void CreateClient_IntegrationWithServiceCollection_Works()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddDurableTaskClient(builder =>
        {
            builder.UseBuildTarget<TestDurableTaskClient, TestClientOptions>();
            builder.Services.Configure<TestClientOptions>(builder.Name, opt => opt.TestProperty = "di-configured");
        });

        ServiceProvider provider = services.BuildServiceProvider();
        IDurableTaskClientFactory factory = provider.GetRequiredService<IDurableTaskClientFactory>();

        // Act
        DurableTaskClient client = factory.CreateClient();

        // Assert
        client.Should().NotBeNull();
        client.Should().BeOfType<TestDurableTaskClient>();
    }

    [Fact]
    public void Factory_RegisteredAsSingleton_ReturnsSameInstance()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddDurableTaskClient(builder =>
        {
            builder.UseBuildTarget<TestDurableTaskClient, TestClientOptions>();
        });

        ServiceProvider provider = services.BuildServiceProvider();

        // Act
        IDurableTaskClientFactory factory1 = provider.GetRequiredService<IDurableTaskClientFactory>();
        IDurableTaskClientFactory factory2 = provider.GetRequiredService<IDurableTaskClientFactory>();

        // Assert
        factory1.Should().BeSameAs(factory2);
    }

    [Fact]
    public void CreateClient_FromFactory_CreatesNewInstanceEachTime()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddDurableTaskClient(builder =>
        {
            builder.UseBuildTarget<TestDurableTaskClient, TestClientOptions>();
        });

        ServiceProvider provider = services.BuildServiceProvider();
        IDurableTaskClientFactory factory = provider.GetRequiredService<IDurableTaskClientFactory>();

        // Act
        DurableTaskClient client1 = factory.CreateClient();
        DurableTaskClient client2 = factory.CreateClient();

        // Assert
        client1.Should().NotBeSameAs(client2);
    }

    [Fact]
    public void Factory_WorksAlongsideProvider()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddDurableTaskClient(builder =>
        {
            builder.UseBuildTarget<TestDurableTaskClient, TestClientOptions>();
            builder.Services.Configure<TestClientOptions>(builder.Name, opt => opt.TestProperty = "test");
        });

        ServiceProvider provider = services.BuildServiceProvider();

        // Act
        IDurableTaskClientFactory factory = provider.GetRequiredService<IDurableTaskClientFactory>();

        // Assert - factory should be resolvable (not testing provider resolution as it requires full client setup)
        factory.Should().NotBeNull();
    }

    #endregion

    #region Disposal Tests

    [Fact]
    public async Task CreateClient_ReturnedClient_CanBeDisposed()
    {
        // Arrange
        DefaultDurableTaskClientFactory.ClientFactoryConfiguration config = new()
        {
            ClientType = typeof(TestDurableTaskClient),
            OptionsType = typeof(TestClientOptions),
        };

        ServiceCollection services = new();
        services.AddOptions<TestClientOptions>();
        ServiceProvider provider = services.BuildServiceProvider();

        DefaultDurableTaskClientFactory factory = new(provider, NullLoggerFactory.Instance, config);

        // Act
        DurableTaskClient client = factory.CreateClient();
        Func<Task> act = async () => await client.DisposeAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateClient_MultipleClients_CanBeDisposedIndependently()
    {
        // Arrange
        DefaultDurableTaskClientFactory.ClientFactoryConfiguration config = new()
        {
            ClientType = typeof(TestDurableTaskClient),
            OptionsType = typeof(TestClientOptions),
        };

        ServiceCollection services = new();
        services.AddOptions<TestClientOptions>();
        ServiceProvider provider = services.BuildServiceProvider();

        DefaultDurableTaskClientFactory factory = new(provider, NullLoggerFactory.Instance, config);

        // Act
        DurableTaskClient client1 = factory.CreateClient("hub1");
        DurableTaskClient client2 = factory.CreateClient("hub2");

        await client1.DisposeAsync();

        // Assert - client2 should still be usable (not disposed)
        client2.Name.Should().Be("hub2");
        await client2.DisposeAsync();
    }

    #endregion

    #region Test Infrastructure

    /// <summary>
    /// Test client options.
    /// </summary>
    public class TestClientOptions : DurableTaskClientOptions
    {
        /// <summary>
        /// Gets or sets a test property.
        /// </summary>
        public string? TestProperty { get; set; }
    }

    /// <summary>
    /// A test DurableTaskClient implementation for unit testing.
    /// </summary>
    public sealed class TestDurableTaskClient : DurableTaskClient
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TestDurableTaskClient"/> class.
        /// </summary>
        /// <param name="name">The name of the client.</param>
        /// <param name="options">The client options.</param>
        /// <param name="logger">The logger.</param>
        public TestDurableTaskClient(string name, TestClientOptions options, ILogger logger)
            : base(name)
        {
            this.Options = options;
            this.Logger = logger;
        }

        /// <summary>
        /// Gets the options.
        /// </summary>
        public TestClientOptions Options { get; }

        /// <summary>
        /// Gets the logger.
        /// </summary>
        public ILogger Logger { get; }

        /// <inheritdoc/>
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        /// <inheritdoc/>
        public override AsyncPageable<OrchestrationMetadata> GetAllInstancesAsync(OrchestrationQuery? filter = null)
            => throw new NotImplementedException();

        /// <inheritdoc/>
        public override Task<OrchestrationMetadata?> GetInstancesAsync(string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
            => throw new NotImplementedException();

        /// <inheritdoc/>
        public override Task<PurgeResult> PurgeAllInstancesAsync(PurgeInstancesFilter filter, PurgeInstanceOptions? options = null, CancellationToken cancellation = default)
            => throw new NotImplementedException();

        /// <inheritdoc/>
        public override Task<PurgeResult> PurgeInstanceAsync(string instanceId, PurgeInstanceOptions? options = null, CancellationToken cancellation = default)
            => throw new NotImplementedException();

        /// <inheritdoc/>
        public override Task RaiseEventAsync(string instanceId, string eventName, object? eventPayload = null, CancellationToken cancellation = default)
            => throw new NotImplementedException();

        /// <inheritdoc/>
        public override Task ResumeInstanceAsync(string instanceId, string? reason = null, CancellationToken cancellation = default)
            => throw new NotImplementedException();

        /// <inheritdoc/>
        public override Task<string> ScheduleNewOrchestrationInstanceAsync(TaskName orchestratorName, object? input = null, StartOrchestrationOptions? options = null, CancellationToken cancellation = default)
            => throw new NotImplementedException();

        /// <inheritdoc/>
        public override Task SuspendInstanceAsync(string instanceId, string? reason = null, CancellationToken cancellation = default)
            => throw new NotImplementedException();

        /// <inheritdoc/>
        public override Task TerminateInstanceAsync(string instanceId, TerminateInstanceOptions? options = null, CancellationToken cancellation = default)
            => throw new NotImplementedException();

        /// <inheritdoc/>
        public override Task<OrchestrationMetadata> WaitForInstanceCompletionAsync(string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
            => throw new NotImplementedException();

        /// <inheritdoc/>
        public override Task<OrchestrationMetadata> WaitForInstanceStartAsync(string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
            => throw new NotImplementedException();
    }

    #endregion
}
