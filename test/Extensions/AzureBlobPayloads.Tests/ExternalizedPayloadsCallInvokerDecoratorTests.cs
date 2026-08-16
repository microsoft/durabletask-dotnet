// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Grpc;
using Microsoft.DurableTask.Client.Grpc.Internal;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.DurableTask.Worker.Grpc.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests;

/// <summary>
/// Verifies that enabling externalized payloads composes with the gRPC transport options instead of
/// replacing them. Previously the extension moved <c>Channel</c> onto an intercepted <c>CallInvoker</c>
/// and nulled <c>Channel</c>, which silently disabled channel recreation on both the worker and the
/// client, and made the <c>Address</c>-only setup unusable.
/// </summary>
public class ExternalizedPayloadsCallInvokerDecoratorTests
{
    static readonly Marshaller<P.CreateInstanceRequest> RequestMarshaller = Marshallers.Create(
        r => r.ToByteArray(), P.CreateInstanceRequest.Parser.ParseFrom);
    static readonly Marshaller<P.CreateInstanceResponse> ResponseMarshaller = Marshallers.Create(
        r => r.ToByteArray(), P.CreateInstanceResponse.Parser.ParseFrom);
    static readonly Method<P.CreateInstanceRequest, P.CreateInstanceResponse> CreateInstanceMethod = new(
        MethodType.Unary,
        "TaskHubSidecarService",
        "StartInstance",
        RequestMarshaller,
        ResponseMarshaller);

    [Fact]
    public void Worker_WithChannel_PreservesChannelSoRecreationStaysEnabled()
    {
        // Arrange
        GrpcChannel channel = GrpcChannel.ForAddress("http://localhost:4001");
        ServiceCollection services = new();
        services.AddSingleton<PayloadStore>(new FakePayloadStore());
        DefaultDurableTaskWorkerBuilder builder = new(null, services);
        builder.UseGrpc(channel);

        // Act
        builder.UseExternalizedPayloads();
        GrpcDurableTaskWorkerOptions options = GetOptions<GrpcDurableTaskWorkerOptions>(services);

        // Assert
        options.Channel.Should().BeSameAs(channel);
    }

    [Fact]
    public void Client_WithChannel_PreservesChannelSoRecreationStaysEnabled()
    {
        // Arrange
        GrpcChannel channel = GrpcChannel.ForAddress("http://localhost:4001");
        ServiceCollection services = new();
        services.AddSingleton<PayloadStore>(new FakePayloadStore());
        DefaultDurableTaskClientBuilder builder = new(null, services);
        builder.UseGrpc(channel);

        // Act
        builder.UseExternalizedPayloads();
        GrpcDurableTaskClientOptions options = GetOptions<GrpcDurableTaskClientOptions>(services);

        // Assert
        options.Channel.Should().BeSameAs(channel);
    }

    [Fact]
    public void Worker_WithAddressOnly_DoesNotThrow()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddSingleton<PayloadStore>(new FakePayloadStore());
        DefaultDurableTaskWorkerBuilder builder = new(null, services);
        builder.UseGrpc("http://localhost:4001");
        builder.UseExternalizedPayloads();

        // Act
        Func<GrpcDurableTaskWorkerOptions> act = () => GetOptions<GrpcDurableTaskWorkerOptions>(services);

        // Assert
        act.Should().NotThrow().Which.Address.Should().Be("http://localhost:4001");
    }

    [Fact]
    public void Client_WithAddressOnly_DoesNotThrow()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddSingleton<PayloadStore>(new FakePayloadStore());
        DefaultDurableTaskClientBuilder builder = new(null, services);
        builder.UseGrpc("http://localhost:4001");
        builder.UseExternalizedPayloads();

        // Act
        Func<GrpcDurableTaskClientOptions> act = () => GetOptions<GrpcDurableTaskClientOptions>(services);

        // Assert
        act.Should().NotThrow().Which.Address.Should().Be("http://localhost:4001");
    }

    [Fact]
    public void Worker_WithExternalCallInvoker_PreservesConfiguredInvoker()
    {
        // Arrange
        CallInvoker external = GrpcChannel.ForAddress("http://localhost:4001").CreateCallInvoker();
        ServiceCollection services = new();
        services.AddSingleton<PayloadStore>(new FakePayloadStore());
        DefaultDurableTaskWorkerBuilder builder = new(null, services);
        builder.UseGrpc(opt => opt.CallInvoker = external);

        // Act
        builder.UseExternalizedPayloads();
        GrpcDurableTaskWorkerOptions options = GetOptions<GrpcDurableTaskWorkerOptions>(services);

        // Assert: the extension no longer mutates the configured invoker; it decorates on use instead.
        options.CallInvoker.Should().BeSameAs(external);
        options.ApplyCallInvokerDecorator(external).Should().NotBeSameAs(external);
    }

    [Fact]
    public void Worker_StillAnnouncesLargePayloadsCapability()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddSingleton<PayloadStore>(new FakePayloadStore());
        DefaultDurableTaskWorkerBuilder builder = new(null, services);
        builder.UseGrpc("http://localhost:4001");

        // Act
        builder.UseExternalizedPayloads();
        GrpcDurableTaskWorkerOptions options = GetOptions<GrpcDurableTaskWorkerOptions>(services);

        // Assert
        options.Capabilities.Should().Contain(P.WorkerCapability.LargePayloads);
    }

    [Fact]
    public async Task Worker_RegisteredDecorator_ExternalizesLargePayloads()
    {
        // Arrange
        ServiceCollection services = new();
        RecordingPayloadStore store = new();
        services.AddSingleton<PayloadStore>(store);
        services.Configure<LargePayloadStorageOptions>(o => o.ThresholdBytes = 1);
        DefaultDurableTaskWorkerBuilder builder = new(null, services);
        builder.UseGrpc("http://localhost:4001");
        builder.UseExternalizedPayloads();
        GrpcDurableTaskWorkerOptions options = GetOptions<GrpcDurableTaskWorkerOptions>(services);

        RecordingCallInvoker inner = new();
        CallInvoker decorated = options.ApplyCallInvokerDecorator(inner);

        // Act
        await InvokeCreateInstanceAsync(decorated, new string('x', 1024));

        // Assert
        store.UploadCount.Should().Be(1);
        inner.LastRequest!.Input.Should().Be(RecordingPayloadStore.Token);
    }

    [Fact]
    public async Task Client_RegisteredDecorator_ExternalizesLargePayloads()
    {
        // Arrange
        ServiceCollection services = new();
        RecordingPayloadStore store = new();
        services.AddSingleton<PayloadStore>(store);
        services.Configure<LargePayloadStorageOptions>(o => o.ThresholdBytes = 1);
        DefaultDurableTaskClientBuilder builder = new(null, services);
        builder.UseGrpc("http://localhost:4001");
        builder.UseExternalizedPayloads();
        GrpcDurableTaskClientOptions options = GetOptions<GrpcDurableTaskClientOptions>(services);

        RecordingCallInvoker inner = new();
        CallInvoker decorated = options.ApplyCallInvokerDecorator(inner);

        // Act
        await InvokeCreateInstanceAsync(decorated, new string('x', 1024));

        // Assert
        store.UploadCount.Should().Be(1);
        inner.LastRequest!.Input.Should().Be(RecordingPayloadStore.Token);
    }

    static Task<P.CreateInstanceResponse> InvokeCreateInstanceAsync(CallInvoker invoker, string input)
    {
        P.CreateInstanceRequest request = new() { InstanceId = "instance", Name = "orchestration", Input = input };
        return invoker.AsyncUnaryCall(CreateInstanceMethod, null, default, request).ResponseAsync;
    }

    static TOptions GetOptions<TOptions>(IServiceCollection services)
        where TOptions : class
    {
        ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptionsMonitor<TOptions>>().Get(null);
    }

    sealed class FakePayloadStore : PayloadStore
    {
        public override Task<string> DownloadAsync(string token, CancellationToken cancellationToken)
            => Task.FromResult(token);

        public override bool IsKnownPayloadToken(string value) => false;

        public override Task<string> UploadAsync(string payLoad, CancellationToken cancellationToken)
            => Task.FromResult(payLoad);
    }

    sealed class RecordingPayloadStore : PayloadStore
    {
        public const string Token = "payload-token";

        int uploadCount;

        public int UploadCount => Volatile.Read(ref this.uploadCount);

        public override Task<string> DownloadAsync(string token, CancellationToken cancellationToken)
            => Task.FromResult(token);

        public override bool IsKnownPayloadToken(string value) => value == Token;

        public override Task<string> UploadAsync(string payLoad, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this.uploadCount);
            return Task.FromResult(Token);
        }
    }

    sealed class RecordingCallInvoker : CallInvoker
    {
        public P.CreateInstanceRequest? LastRequest { get; private set; }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            this.LastRequest = request as P.CreateInstanceRequest;
            return new AsyncUnaryCall<TResponse>(
                Task.FromResult(Activator.CreateInstance<TResponse>()),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
            => throw new NotSupportedException();

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options)
            => throw new NotSupportedException();

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options)
            => throw new NotSupportedException();

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
            => throw new NotSupportedException();
    }
}
