// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using P = Microsoft.DurableTask.Protobuf;
#if NET6_0_OR_GREATER
using Microsoft.DurableTask;
using Microsoft.DurableTask.ScheduledTasks;
#endif

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

/// <summary>
/// Tests for worker capabilities configuration.
/// </summary>
public class WorkerCapabilitiesTests
{
    [Fact]
    public void DefaultCapabilities_IncludesHistoryStreaming()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new(null, services);
        builder.UseGrpc();

        // Act
        IServiceProvider provider = services.BuildServiceProvider();
        GrpcDurableTaskWorkerOptions options = provider.GetOptions<GrpcDurableTaskWorkerOptions>();

        // Assert
        options.Capabilities.Should().NotBeNull();
        options.Capabilities.Should().Contain(P.WorkerCapability.HistoryStreaming);
        options.Capabilities.Should().HaveCount(1);
    }

    [Fact]
    public void Capabilities_CanBeManuallyAdded()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new(null, services);
        builder.UseGrpc(opt =>
        {
            opt.Capabilities.Add(P.WorkerCapability.ScheduledTasks);
            opt.Capabilities.Add(P.WorkerCapability.LargePayloads);
        });

        // Act
        IServiceProvider provider = services.BuildServiceProvider();
        GrpcDurableTaskWorkerOptions options = provider.GetOptions<GrpcDurableTaskWorkerOptions>();

        // Assert
        options.Capabilities.Should().NotBeNull();
        options.Capabilities.Should().Contain(P.WorkerCapability.HistoryStreaming);
        options.Capabilities.Should().Contain(P.WorkerCapability.ScheduledTasks);
        options.Capabilities.Should().Contain(P.WorkerCapability.LargePayloads);
        options.Capabilities.Should().HaveCount(3);
    }

    [Fact]
    public void Capabilities_CanBeAddedViaPostConfigure()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new(null, services);
        builder.UseGrpc();
        
        // Add capability via PostConfigure
        services.Configure<GrpcDurableTaskWorkerOptions>(builder.Name, opt =>
        {
            opt.Capabilities.Add(P.WorkerCapability.ScheduledTasks);
        });

        // Act
        IServiceProvider provider = services.BuildServiceProvider();
        GrpcDurableTaskWorkerOptions options = provider.GetOptions<GrpcDurableTaskWorkerOptions>();

        // Assert
        options.Capabilities.Should().NotBeNull();
        options.Capabilities.Should().Contain(P.WorkerCapability.HistoryStreaming);
        options.Capabilities.Should().Contain(P.WorkerCapability.ScheduledTasks);
        options.Capabilities.Should().HaveCount(2);
    }

    [Fact]
    public void Capabilities_AddingDuplicateDoesNotCreateDuplicates()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new(null, services);
        builder.UseGrpc(opt =>
        {
            // HistoryStreaming is already in the default set
            opt.Capabilities.Add(P.WorkerCapability.HistoryStreaming);
            opt.Capabilities.Add(P.WorkerCapability.HistoryStreaming);
        });

        // Act
        IServiceProvider provider = services.BuildServiceProvider();
        GrpcDurableTaskWorkerOptions options = provider.GetOptions<GrpcDurableTaskWorkerOptions>();

        // Assert
        options.Capabilities.Should().NotBeNull();
        options.Capabilities.Should().Contain(P.WorkerCapability.HistoryStreaming);
        // HashSet prevents duplicates
        options.Capabilities.Should().HaveCount(1);
    }

    [Fact]
    public void Capabilities_CanBeClearedAndReset()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new(null, services);
        builder.UseGrpc(opt =>
        {
            opt.Capabilities.Clear();
            opt.Capabilities.Add(P.WorkerCapability.ScheduledTasks);
        });

        // Act
        IServiceProvider provider = services.BuildServiceProvider();
        GrpcDurableTaskWorkerOptions options = provider.GetOptions<GrpcDurableTaskWorkerOptions>();

        // Assert
        options.Capabilities.Should().NotBeNull();
        options.Capabilities.Should().NotContain(P.WorkerCapability.HistoryStreaming);
        options.Capabilities.Should().Contain(P.WorkerCapability.ScheduledTasks);
        options.Capabilities.Should().HaveCount(1);
    }

    [Fact]
    public void Capabilities_AreIndependentPerBuilder()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder1 = new("worker1", services);
        DefaultDurableTaskWorkerBuilder builder2 = new("worker2", services);
        
        builder1.UseGrpc(opt => opt.Capabilities.Add(P.WorkerCapability.ScheduledTasks));
        builder2.UseGrpc(opt => opt.Capabilities.Add(P.WorkerCapability.LargePayloads));

        // Act
        IServiceProvider provider = services.BuildServiceProvider();
        GrpcDurableTaskWorkerOptions options1 = provider.GetOptions<GrpcDurableTaskWorkerOptions>("worker1");
        GrpcDurableTaskWorkerOptions options2 = provider.GetOptions<GrpcDurableTaskWorkerOptions>("worker2");

        // Assert
        options1.Capabilities.Should().Contain(P.WorkerCapability.HistoryStreaming);
        options1.Capabilities.Should().Contain(P.WorkerCapability.ScheduledTasks);
        options1.Capabilities.Should().NotContain(P.WorkerCapability.LargePayloads);

        options2.Capabilities.Should().Contain(P.WorkerCapability.HistoryStreaming);
        options2.Capabilities.Should().Contain(P.WorkerCapability.LargePayloads);
        options2.Capabilities.Should().NotContain(P.WorkerCapability.ScheduledTasks);
    }

#if NET6_0_OR_GREATER
    [Fact]
    public void UseScheduledTasks_AddsScheduledTasksCapability()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new(null, services);
        builder.UseGrpc();
        builder.UseScheduledTasks();

        // Act
        IServiceProvider provider = services.BuildServiceProvider();
        GrpcDurableTaskWorkerOptions options = provider.GetOptions<GrpcDurableTaskWorkerOptions>();

        // Assert
        options.Capabilities.Should().NotBeNull();
        options.Capabilities.Should().Contain(P.WorkerCapability.HistoryStreaming);
        options.Capabilities.Should().Contain(P.WorkerCapability.ScheduledTasks);
        options.Capabilities.Should().HaveCount(2);
    }

    [Fact]
    public void UseScheduledTasks_WithExistingCapabilities_PreservesOtherCapabilities()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new(null, services);
        builder.UseGrpc(opt =>
        {
            opt.Capabilities.Add(P.WorkerCapability.LargePayloads);
        });
        builder.UseScheduledTasks();

        // Act
        IServiceProvider provider = services.BuildServiceProvider();
        GrpcDurableTaskWorkerOptions options = provider.GetOptions<GrpcDurableTaskWorkerOptions>();

        // Assert
        options.Capabilities.Should().NotBeNull();
        options.Capabilities.Should().Contain(P.WorkerCapability.HistoryStreaming);
        options.Capabilities.Should().Contain(P.WorkerCapability.ScheduledTasks);
        options.Capabilities.Should().Contain(P.WorkerCapability.LargePayloads);
        options.Capabilities.Should().HaveCount(3);
    }

    [Fact]
    public void UseExternalizedPayloads_AddsLargePayloadsCapability()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new(null, services);
        GrpcChannel channel = GetChannel();
        builder.UseGrpc(channel);
        
        // Register required dependencies for UseExternalizedPayloads
        services.AddSingleton<PayloadStore>(new TestPayloadStore());
        services.Configure<LargePayloadStorageOptions>(builder.Name, opt =>
        {
            opt.ContainerName = "test";
            opt.ConnectionString = "UseDevelopmentStorage=true";
        });
        
        builder.UseExternalizedPayloads();

        // Act
        IServiceProvider provider = services.BuildServiceProvider();
        GrpcDurableTaskWorkerOptions options = provider.GetOptions<GrpcDurableTaskWorkerOptions>();

        // Assert
        options.Capabilities.Should().NotBeNull();
        options.Capabilities.Should().Contain(P.WorkerCapability.HistoryStreaming);
        options.Capabilities.Should().Contain(P.WorkerCapability.LargePayloads);
        options.Capabilities.Should().HaveCount(2);
    }

    [Fact]
    public void UseExternalizedPayloads_WithExistingCapabilities_PreservesOtherCapabilities()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new(null, services);
        GrpcChannel channel = GetChannel();
        builder.UseGrpc(opt =>
        {
            opt.Channel = channel;
            opt.Capabilities.Add(P.WorkerCapability.ScheduledTasks);
        });
        
        // Register required dependencies for UseExternalizedPayloads
        services.AddSingleton<PayloadStore>(new TestPayloadStore());
        services.Configure<LargePayloadStorageOptions>(builder.Name, opt =>
        {
            opt.ContainerName = "test";
            opt.ConnectionString = "UseDevelopmentStorage=true";
        });
        
        builder.UseExternalizedPayloads();

        // Act
        IServiceProvider provider = services.BuildServiceProvider();
        GrpcDurableTaskWorkerOptions options = provider.GetOptions<GrpcDurableTaskWorkerOptions>();

        // Assert
        options.Capabilities.Should().NotBeNull();
        options.Capabilities.Should().Contain(P.WorkerCapability.HistoryStreaming);
        options.Capabilities.Should().Contain(P.WorkerCapability.ScheduledTasks);
        options.Capabilities.Should().Contain(P.WorkerCapability.LargePayloads);
        options.Capabilities.Should().HaveCount(3);
    }

    static GrpcChannel GetChannel() => GrpcChannel.ForAddress("http://localhost:9001");

    sealed class TestPayloadStore : PayloadStore
    {
        public override Task<string> UploadAsync(string payLoad, CancellationToken cancellationToken)
        {
            return Task.FromResult($"token:{Guid.NewGuid()}");
        }

        public override Task<string> DownloadAsync(string token, CancellationToken cancellationToken)
        {
            return Task.FromResult(string.Empty);
        }

        public override bool IsKnownPayloadToken(string value)
        {
            return value.StartsWith("token:", StringComparison.Ordinal);
        }
    }
#endif
}

