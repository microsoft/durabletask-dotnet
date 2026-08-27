// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Grpc;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests;

/// <summary>
/// Verifies that enabling externalized payloads composes with the gRPC transport options instead of
/// replacing them. Previously the extension moved <c>Channel</c> onto an intercepted <c>CallInvoker</c>
/// and nulled <c>Channel</c>, which silently disabled channel recreation on both the worker and the
/// client, and made the <c>Address</c>-only setup unusable. It now registers an interceptor on the
/// supported <c>Interceptors</c> collection, which the worker and client apply to every invoker they
/// build.
/// </summary>
public class ExternalizedPayloadsInterceptorTests
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
        using GrpcChannel channel = GrpcChannel.ForAddress("http://localhost:4001");
        ServiceCollection services = new();
        services.AddSingleton<PayloadStore>(new FakePayloadStore());
        DefaultDurableTaskWorkerBuilder builder = new(null, services);
        builder.UseGrpc(channel);

        // Act
        builder.UseExternalizedPayloads();
        GrpcDurableTaskWorkerOptions options = GetOptions<GrpcDurableTaskWorkerOptions>(services);

        // Assert
        options.Channel.Should().BeSameAs(channel);
        options.Interceptors.Should().ContainSingle().Which.Should().BeOfType<AzureBlobPayloadsSideCarInterceptor>();
    }

    [Fact]
    public void Client_WithChannel_PreservesChannelSoRecreationStaysEnabled()
    {
        // Arrange
        using GrpcChannel channel = GrpcChannel.ForAddress("http://localhost:4001");
        ServiceCollection services = new();
        services.AddSingleton<PayloadStore>(new FakePayloadStore());
        DefaultDurableTaskClientBuilder builder = new(null, services);
        builder.UseGrpc(channel);

        // Act
        builder.UseExternalizedPayloads();
        GrpcDurableTaskClientOptions options = GetOptions<GrpcDurableTaskClientOptions>(services);

        // Assert
        options.Channel.Should().BeSameAs(channel);
        options.Interceptors.Should().ContainSingle().Which.Should().BeOfType<AzureBlobPayloadsSideCarInterceptor>();
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
        using GrpcChannel channel = GrpcChannel.ForAddress("http://localhost:4001");
        CallInvoker external = channel.CreateCallInvoker();
        ServiceCollection services = new();
        services.AddSingleton<PayloadStore>(new FakePayloadStore());
        DefaultDurableTaskWorkerBuilder builder = new(null, services);
        builder.UseGrpc(opt => opt.CallInvoker = external);

        // Act
        builder.UseExternalizedPayloads();
        GrpcDurableTaskWorkerOptions options = GetOptions<GrpcDurableTaskWorkerOptions>(services);

        // Assert: the extension no longer mutates the configured invoker; it intercepts on use instead.
        options.CallInvoker.Should().BeSameAs(external);
        options.Interceptors.Should().ContainSingle().Which.Should().BeOfType<AzureBlobPayloadsSideCarInterceptor>();
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
    public async Task Worker_RegisteredInterceptor_ExternalizesLargePayloads()
    {
        // Arrange
        RecordingCallInvoker inner = new();
        RecordingPayloadStore store = new();
        ServiceProvider provider = BuildWorkerProvider(inner, store);

        // Act: build the invoker the same way the running worker does.
        CallInvoker invoker = BuildWorkerCallInvoker(provider);
        await InvokeCreateInstanceAsync(invoker, new string('x', 1024));

        // Assert
        store.UploadCount.Should().Be(1);
        inner.LastRequest!.Input.Should().Be(RecordingPayloadStore.Token);
    }

    [Fact]
    public async Task Client_RegisteredInterceptor_ExternalizesLargePayloads()
    {
        // Arrange
        RecordingCallInvoker inner = new();
        RecordingPayloadStore store = new();
        ServiceCollection services = new();
        services.AddSingleton<PayloadStore>(store);
        services.Configure<LargePayloadStorageOptions>(o => o.ThresholdBytes = 1);
        DefaultDurableTaskClientBuilder builder = new(null, services);
        builder.UseGrpc(opt => opt.CallInvoker = inner);
        builder.UseExternalizedPayloads();
        GrpcDurableTaskClientOptions options = GetOptions<GrpcDurableTaskClientOptions>(services);

        // Act: build the invoker the same way the running client does.
        CallInvoker invoker = BuildClientCallInvoker(options);
        await InvokeCreateInstanceAsync(invoker, new string('x', 1024));

        // Assert
        store.UploadCount.Should().Be(1);
        inner.LastRequest!.Input.Should().Be(RecordingPayloadStore.Token);
    }

    static Task<P.CreateInstanceResponse> InvokeCreateInstanceAsync(CallInvoker invoker, string input)
    {
        P.CreateInstanceRequest request = new() { InstanceId = "instance", Name = "orchestration", Input = input };
        return invoker.AsyncUnaryCall(CreateInstanceMethod, null, default, request).ResponseAsync;
    }

    static ServiceProvider BuildWorkerProvider(CallInvoker inner, PayloadStore store)
    {
        ServiceCollection services = new();
        services.AddSingleton(store);
        services.Configure<LargePayloadStorageOptions>(o => o.ThresholdBytes = 1);
        DefaultDurableTaskWorkerBuilder builder = new(null, services);
        builder.UseGrpc(opt => opt.CallInvoker = inner);
        builder.UseExternalizedPayloads();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Builds a <see cref="CallInvoker"/> through the worker's own private invoker-building path, so the
    /// test cannot accidentally re-implement how interceptors are applied.
    /// </summary>
    /// <param name="provider">The configured service provider.</param>
    /// <returns>The invoker the worker would use.</returns>
    static CallInvoker BuildWorkerCallInvoker(ServiceProvider provider)
    {
        Type workerType = typeof(GrpcDurableTaskWorkerOptions).Assembly
            .GetType("Microsoft.DurableTask.Worker.Grpc.GrpcDurableTaskWorker", throwOnError: true)!;

        object worker = Activator.CreateInstance(
            workerType,
            new object?[]
            {
                string.Empty,
                Mock.Of<IDurableTaskFactory>(),
                provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskWorkerOptions>>(),
                provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerOptions>>(),
                provider,
                NullLoggerFactory.Instance,
                null,
                null,
                null,
            })!;

        MethodInfo getCallInvoker = workerType.GetMethod(
            "GetCallInvoker", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object?[] args = { null, null };
        getCallInvoker.Invoke(worker, args);
        return (CallInvoker)args[0]!;
    }

    /// <summary>
    /// Builds a <see cref="CallInvoker"/> through the client's own private invoker-building path, so the
    /// test cannot accidentally re-implement how interceptors are applied.
    /// </summary>
    /// <param name="options">The configured client options.</param>
    /// <returns>The invoker the client would use.</returns>
    static CallInvoker BuildClientCallInvoker(GrpcDurableTaskClientOptions options)
    {
        MethodInfo getCallInvoker = typeof(GrpcDurableTaskClient).GetMethod(
            "GetCallInvoker", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] args = { options, NullLogger.Instance, null };
        getCallInvoker.Invoke(null, args);
        return (CallInvoker)args[2]!;
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
