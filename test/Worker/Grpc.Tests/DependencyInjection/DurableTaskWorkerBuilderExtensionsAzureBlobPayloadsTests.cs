// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Converters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

public class DurableTaskWorkerBuilderExtensionsAzureBlobPayloadsTests
{
    [Fact]
    public void UseExternalizedPayloads_AddsLargePayloadsCapability()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        
        // Configure a minimal gRPC worker with channel
        GrpcChannel channel = GetChannel();
        builder.UseGrpc(opt => opt.Channel = channel);
        
        // Register a fake PayloadStore
        services.AddSingleton<PayloadStore>(new FakePayloadStore());
        
        // Configure storage options
        services.Configure<LargePayloadStorageOptions>("test", opts =>
        {
            opts.ContainerName = "test";
            opts.ConnectionString = "UseDevelopmentStorage=true";
        });

        // Act
        builder.UseExternalizedPayloads();

        // Assert
        IServiceProvider provider = services.BuildServiceProvider();
        GrpcDurableTaskWorkerOptions options = provider.GetService<IOptionsMonitor<GrpcDurableTaskWorkerOptions>>()!.Get("test");
        
        options.Capabilities.Should().Contain(P.WorkerCapability.LargePayloads);
    }

    [Fact]
    public void UseExternalizedPayloads_WithConfigure_AddsLargePayloadsCapability()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        
        // Configure a minimal gRPC worker with channel
        GrpcChannel channel = GetChannel();
        builder.UseGrpc(opt => opt.Channel = channel);
        
        // Register a fake PayloadStore
        services.AddSingleton<PayloadStore>(new FakePayloadStore());

        // Act
        builder.UseExternalizedPayloads(opts =>
        {
            opts.ContainerName = "test";
            opts.ConnectionString = "UseDevelopmentStorage=true";
        });

        // Assert
        IServiceProvider provider = services.BuildServiceProvider();
        GrpcDurableTaskWorkerOptions options = provider.GetService<IOptionsMonitor<GrpcDurableTaskWorkerOptions>>()!.Get("test");
        
        options.Capabilities.Should().Contain(P.WorkerCapability.LargePayloads);
    }

#if NET6_0_OR_GREATER
    static GrpcChannel GetChannel() => GrpcChannel.ForAddress("http://localhost:9001");
#endif

#if NETFRAMEWORK
    static GrpcChannel GetChannel() => new("http://localhost:9001", ChannelCredentials.Insecure);
#endif

    class FakePayloadStore : PayloadStore
    {
        public override Task<string> DownloadAsync(string token, CancellationToken cancellationToken)
        {
            return Task.FromResult("fake");
        }

        public override bool IsKnownPayloadToken(string value)
        {
            return value.StartsWith("fake:");
        }

        public override Task<string> UploadAsync(string payLoad, CancellationToken cancellationToken)
        {
            return Task.FromResult("fake:token");
        }
    }
}
