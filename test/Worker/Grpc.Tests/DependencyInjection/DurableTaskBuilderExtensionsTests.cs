// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

public class DurableTaskBuilderExtensionsTests
{
    [Fact]
    public void UseGrpc_Sets()
    {
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new(null, services);
        Action act = () => builder.UseGrpc();
        act.Should().NotThrow();
        builder.BuildTarget.Should().Be(typeof(GrpcDurableTaskWorker));

        IServiceProvider provider = services.BuildServiceProvider();
        GrpcDurableTaskWorkerOptions options = provider.GetOptions<GrpcDurableTaskWorkerOptions>();

        options.Address.Should().BeNull();
        options.Channel.Should().BeNull();
    }

    [Fact]
    public void UseGrpc_Address_Sets()
    {
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new(null, services);
        builder.UseGrpc("localhost:9001");

        IServiceProvider provider = services.BuildServiceProvider();
        GrpcDurableTaskWorkerOptions options = provider.GetOptions<GrpcDurableTaskWorkerOptions>();

        options.Address.Should().Be("localhost:9001");
        options.Channel.Should().BeNull();
    }

    [Fact]
    public void UseGrpc_Channel_Sets()
    {
        GrpcChannel c = GetChannel();
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new(null, services);
        builder.UseGrpc(c);

        IServiceProvider provider = services.BuildServiceProvider();
        GrpcDurableTaskWorkerOptions options = provider.GetOptions<GrpcDurableTaskWorkerOptions>();

        options.Channel.Should().Be(c);
        options.Address.Should().BeNull();
    }

    [Fact]
    public void UseGrpc_Callback_Sets()
    {
        GrpcChannel c = GetChannel();
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new(null, services);
        builder.UseGrpc(opt => opt.Channel = c);

        IServiceProvider provider = services.BuildServiceProvider();
        GrpcDurableTaskWorkerOptions options = provider.GetOptions<GrpcDurableTaskWorkerOptions>();

        options.Channel.Should().Be(c);
        options.Address.Should().BeNull();
    }

#if NET6_0_OR_GREATER
    static GrpcChannel GetChannel() => GrpcChannel.ForAddress("http://localhost:9001");
#endif

#if NETFRAMEWORK
    static GrpcChannel GetChannel() => new("http://localhost:9001", ChannelCredentials.Insecure);
#endif
}
