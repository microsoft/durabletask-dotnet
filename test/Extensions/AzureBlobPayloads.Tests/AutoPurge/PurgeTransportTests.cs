// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.DurableTask.Worker.Grpc.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Microsoft.DurableTask.Protobuf.LargePayloads.LargePayloadPurge;
using LP = Microsoft.DurableTask.Protobuf.LargePayloads;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests.AutoPurge;

/// <summary>
/// Covers how the purge activities reach the backend. They share the worker's transport rather than opening a
/// second connection, and that transport moves: the worker replaces its channel when the current one is wedged,
/// and the invoker it hands out is the one left after the configured interceptors have run. A client that
/// captured an invoker at registration time would miss both.
/// </summary>
public class PurgeTransportTests
{
    [Fact]
    public void AddressOnlyWorker_ResolvesThePurgeClient()
    {
        // Arrange - Address-only is a supported worker configuration and the one that leaves both Channel and
        // CallInvoker null. Resolving the purge client used to read those two properties and throw here.
        ServiceCollection services = new();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddDurableTaskWorker(builder =>
        {
            builder.UseGrpc(options => options.Address = "http://localhost:4001");
            builder.UseExternalizedPayloads(options => options.ConnectionString = "UseDevelopmentStorage=true");
        });

        using ServiceProvider provider = services.BuildServiceProvider();

        // Act
        Func<LargePayloadPurgeClient> resolve = () => provider.GetRequiredService<LargePayloadPurgeClient>();

        // Assert
        resolve.Should().NotThrow();
    }

    [Fact]
    public async Task RunningWorker_RoutesPurgeCallsThroughItsOwnInterceptedTransport()
    {
        // Arrange - one worker, one transport. The interceptor stands in for the configured cross-cutting chain
        // (authentication is the one that matters in production): if the purge client were built on a raw
        // invoker instead of the worker's effective one, the interceptor would see the sidecar RPCs and none of
        // the purge RPCs.
        RecordingInterceptor interceptor = new();
        FakeCallInvoker transport = new();
        ServiceCollection services = new();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddDurableTaskWorker(builder =>
        {
            builder.UseGrpc(options =>
            {
                options.CallInvoker = transport;
                options.Interceptors.Add(interceptor);
            });
            builder.UseExternalizedPayloads(options =>
            {
                options.ConnectionString = "UseDevelopmentStorage=true";
                options.AutoPurge = true;
            });
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        LargePayloadPurgeClient client = provider.GetRequiredService<LargePayloadPurgeClient>();
        IHostedService worker = provider.GetServices<IHostedService>().Single();

        // Act - start the worker and wait until it has actually opened the work-item stream, which is the point
        // at which activities could start running. Then issue the two purge RPCs an activity would issue.
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await transport.WorkItemsRequested.Task.WaitAsync(TimeSpan.FromSeconds(30));

            await client.GetLargePayloadTombstonesAsync(new LP.GetLargePayloadTombstonesRequest { Limit = 1 });
            await client.ReportLargePayloadPurgeResultsAsync(new LP.ReportLargePayloadPurgeResultsRequest());
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        // Assert - the configured interceptor observed the worker's own connection-setup RPCs AND both purge
        // RPCs, so all four rode the same intercepted transport.
        interceptor.Calls.Should().Contain("Hello");
        interceptor.Calls.Should().Contain("SetLargePayloadAutoPurge");
        interceptor.Calls.Should().Contain("GetLargePayloadTombstones");
        interceptor.Calls.Should().Contain("ReportLargePayloadPurgeResults");
    }

    [Fact]
    public async Task WhenTheWorkerRecreatesItsChannel_AnExistingPurgeClientFollowsIt()
    {
        // Arrange - two channels backed by handlers that record which one received a call. The worker starts on
        // A, is forced to recreate onto B, and the purge client is resolved ONCE up front: the property under
        // test is that an already-constructed client routes its later calls through the replacement.
        RecordingHandler handlerA = new();
        RecordingHandler handlerB = new();
        using GrpcChannel channelA = CreateChannel("http://localhost:14001", handlerA);
        using GrpcChannel channelB = CreateChannel("http://localhost:14002", handlerB);

        // The recreator hands over B exactly once, and only when the test releases the gate, so the window in
        // which A is the published transport is bounded by the test rather than by timing. Later requests park
        // until shutdown so the connect loop cannot spin against a channel that can never succeed.
        TaskCompletionSource<bool> releaseB = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int recreateCalls = 0;

        ServiceCollection services = new();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddDurableTaskWorker(builder =>
        {
            builder.UseGrpc(options =>
            {
                options.Channel = channelA;
                options.Internal.ChannelRecreateFailureThreshold = 1;
                options.SetChannelRecreator(async (current, cancellation) =>
                {
                    if (Interlocked.Increment(ref recreateCalls) == 1)
                    {
                        await releaseB.Task.WaitAsync(cancellation);
                        return channelB;
                    }

                    await Task.Delay(Timeout.Infinite, cancellation);
                    return current;
                });
            });
            builder.UseExternalizedPayloads(options => options.ConnectionString = "UseDevelopmentStorage=true");
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        LargePayloadPurgeClient client = provider.GetRequiredService<LargePayloadPurgeClient>();
        IHostedService worker = provider.GetServices<IHostedService>().Single();

        // Act
        await worker.StartAsync(CancellationToken.None);
        try
        {
            // The worker's Hello landing on A proves A is the published transport, with no polling or sleeping.
            await handlerA.FirstHello.WaitAsync(TimeSpan.FromSeconds(30));
            await FetchTombstonesIgnoringTransportFailureAsync(client);

            int fetchesOnAWhileAWasCurrent = handlerA.PurgeFetchCount;
            int fetchesOnBWhileAWasCurrent = handlerB.PurgeFetchCount;

            // Let the recreate complete; Hello landing on B proves the replacement has been published.
            releaseB.SetResult(true);
            await handlerB.FirstHello.WaitAsync(TimeSpan.FromSeconds(30));
            await FetchTombstonesIgnoringTransportFailureAsync(client);

            // Assert - the same client instance moved from A to B, and did not keep a second call on A.
            fetchesOnAWhileAWasCurrent.Should().Be(1);
            fetchesOnBWhileAWasCurrent.Should().Be(0);
            handlerB.PurgeFetchCount.Should().Be(1);
            handlerA.PurgeFetchCount.Should().Be(1);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RebindableCallInvoker_MovesAnAlreadyConstructedClientToTheReplacement()
    {
        // Arrange - the generated client caches the invoker it was constructed with, so this is the mechanism
        // that lets a singleton client follow the worker without being rebuilt.
        RebindableCallInvoker invoker = new();
        FakeCallInvoker first = new();
        FakeCallInvoker second = new();
        LargePayloadPurgeClient client = new(invoker);

        // Act
        invoker.Rebind(first);
        await client.GetLargePayloadTombstonesAsync(new LP.GetLargePayloadTombstonesRequest { Limit = 1 });
        invoker.Rebind(second);
        await client.GetLargePayloadTombstonesAsync(new LP.GetLargePayloadTombstonesRequest { Limit = 1 });

        // Assert
        first.Calls.Should().ContainSingle().Which.Should().Be("GetLargePayloadTombstones");
        second.Calls.Should().ContainSingle().Which.Should().Be("GetLargePayloadTombstones");
    }

    [Fact]
    public void RebindableCallInvoker_BeforeAnythingIsPublished_Throws()
    {
        // Arrange - reaching a purge call with no worker transport is a wiring defect. Surfacing it beats
        // inventing a connection or returning a null the caller would dereference somewhere less obvious.
        RebindableCallInvoker invoker = new();
        LargePayloadPurgeClient client = new(invoker);

        // Act
        Func<Task> call = () =>
            client.GetLargePayloadTombstonesAsync(new LP.GetLargePayloadTombstonesRequest { Limit = 1 }).ResponseAsync;

        // Assert
        call.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void RebindableCallInvoker_ForwardsEveryCallShape()
    {
        // Arrange - CallInvoker has five abstract members and the generated clients only exercise two of them
        // today. Cover the rest so a future streaming RPC on the purge service cannot silently bypass the
        // indirection.
        RebindableCallInvoker invoker = new();
        ShapeRecordingCallInvoker target = new();
        invoker.Rebind(target);
        Method<Empty, Empty> method = new(
            MethodType.Unary,
            "svc",
            "m",
            Marshallers.Create(_ => Array.Empty<byte>(), _ => new Empty()),
            Marshallers.Create(_ => Array.Empty<byte>(), _ => new Empty()));

        // Act
        invoker.BlockingUnaryCall(method, null, default, new Empty());
        invoker.AsyncUnaryCall(method, null, default, new Empty());
        invoker.AsyncServerStreamingCall(method, null, default, new Empty());
        invoker.AsyncClientStreamingCall(method, null, default);
        invoker.AsyncDuplexStreamingCall(method, null, default);

        // Assert
        target.Shapes.Should().Equal("blocking", "unary", "server", "client", "duplex");
    }

    static GrpcChannel CreateChannel(string address, HttpMessageHandler handler)
        => GrpcChannel.ForAddress(address, new GrpcChannelOptions { HttpHandler = handler });

    static async Task FetchTombstonesIgnoringTransportFailureAsync(LargePayloadPurgeClient client)
    {
        // The fake endpoints never answer, so the call always fails. Which handler recorded it is the signal,
        // not whether it succeeded.
        try
        {
            await client.GetLargePayloadTombstonesAsync(new LP.GetLargePayloadTombstonesRequest { Limit = 1 });
        }
        catch (RpcException)
        {
        }
    }

    /// <summary>
    /// Records the gRPC method names that pass through the configured interceptor chain.
    /// </summary>
    sealed class RecordingInterceptor : Interceptor
    {
        readonly ConcurrentQueue<string> calls = new();

        public IReadOnlyCollection<string> Calls => this.calls.ToArray();

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            this.calls.Enqueue(context.Method.Name);
            return continuation(request, context);
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
        {
            this.calls.Enqueue(context.Method.Name);
            return continuation(request, context);
        }
    }

    /// <summary>
    /// A transport that answers the worker's connection-setup RPCs and the purge RPCs, and parks the work-item
    /// stream so the worker stays connected for the duration of a test.
    /// </summary>
    sealed class FakeCallInvoker : CallInvoker
    {
        readonly ConcurrentQueue<string> calls = new();

        public IReadOnlyCollection<string> Calls => this.calls.ToArray();

        public TaskCompletionSource<bool> WorkItemsRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
            => (TResponse)this.Respond(method.Name);

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            return new AsyncUnaryCall<TResponse>(
                Task.FromResult((TResponse)this.Respond(method.Name)),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            this.calls.Enqueue(method.Name);
            this.WorkItemsRequested.TrySetResult(true);
            return new AsyncServerStreamingCall<TResponse>(
                new ParkedStreamReader<TResponse>(),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });
        }

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options)
            => throw new NotSupportedException();

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options)
            => throw new NotSupportedException();

        object Respond(string methodName)
        {
            this.calls.Enqueue(methodName);
            return methodName switch
            {
                "Hello" => new Empty(),
                "SetLargePayloadAutoPurge" => new LP.SetLargePayloadAutoPurgeResponse(),
                "GetLargePayloadTombstones" => new LP.GetLargePayloadTombstonesResponse(),
                "ReportLargePayloadPurgeResults" => new LP.ReportLargePayloadPurgeResultsResponse(),
                _ => throw new RpcException(new Status(StatusCode.Unimplemented, methodName)),
            };
        }
    }

    /// <summary>
    /// Records which <see cref="CallInvoker"/> member was forwarded, so every call shape can be covered.
    /// </summary>
    sealed class ShapeRecordingCallInvoker : CallInvoker
    {
        public List<string> Shapes { get; } = new();

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            this.Shapes.Add("blocking");
            return default!;
        }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            this.Shapes.Add("unary");
            return new AsyncUnaryCall<TResponse>(
                Task.FromResult(default(TResponse)!),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            this.Shapes.Add("server");
            return new AsyncServerStreamingCall<TResponse>(
                new ParkedStreamReader<TResponse>(),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });
        }

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options)
        {
            this.Shapes.Add("client");
            return new AsyncClientStreamingCall<TRequest, TResponse>(
                null!,
                Task.FromResult(default(TResponse)!),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });
        }

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options)
        {
            this.Shapes.Add("duplex");
            return new AsyncDuplexStreamingCall<TRequest, TResponse>(
                null!,
                new ParkedStreamReader<TResponse>(),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });
        }
    }

    /// <summary>
    /// A work-item stream that never yields a message and completes only when the reader is cancelled.
    /// </summary>
    sealed class ParkedStreamReader<T> : IAsyncStreamReader<T>
    {
        public T Current => default!;

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return false;
        }
    }

    /// <summary>
    /// A channel-backing handler that records the gRPC method path of every request and answers every call with
    /// a trailers-only <see cref="StatusCode.Unavailable"/>, so a call can be attributed to one channel without
    /// any network I/O and the worker's connect loop classifies the failure the same way every run.
    /// </summary>
    sealed class RecordingHandler : HttpMessageHandler
    {
        readonly ConcurrentQueue<string> paths = new();
        readonly TaskCompletionSource<bool> firstHello = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FirstHello => this.firstHello.Task;

        public int PurgeFetchCount =>
            this.paths.Count(p => p.EndsWith("/GetLargePayloadTombstones", StringComparison.Ordinal));

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            this.paths.Enqueue(path);

            // Returning a real gRPC status rather than throwing keeps the classification out of the transport
            // library's exception-mapping rules: the worker must see Unavailable for the connect loop to count
            // the failure toward a channel recreate.
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Version = new Version(2, 0),
                Content = new ByteArrayContent(Array.Empty<byte>()),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            response.Headers.TryAddWithoutValidation("grpc-status", ((int)StatusCode.Unavailable).ToString(CultureInfo.InvariantCulture));
            response.Headers.TryAddWithoutValidation("grpc-message", "This endpoint intentionally never answers.");

            if (path.EndsWith("/Hello", StringComparison.Ordinal))
            {
                this.firstHello.TrySetResult(true);
            }

            return Task.FromResult(response);
        }
    }
}
