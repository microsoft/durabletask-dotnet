// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.DurableTask.Tests.Logging;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;
using P = Microsoft.DurableTask.Protobuf;
using LP = Microsoft.DurableTask.Protobuf.LargePayloads;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

/// <summary>
/// The auto-purge opt-in is announced with a dedicated <c>SetLargePayloadAutoPurge</c> RPC on a service only the
/// Durable Task Scheduler implements, sent once per connection before the work-item stream is requested. Two
/// properties matter and neither is visible from the type system: the RPC must not be sent when the worker has
/// no opinion, and no failure of it may reach ConnectAsync's caller - that caller drives the reconnect and
/// channel-recreate counters, so an escaping exception would turn an optional cleanup feature into an
/// orchestration execution outage on every backend that does not implement the service.
/// </summary>
public class LargePayloadAutoPurgeConnectTests
{
    const string SetAutoPurgeMethod =
        "/microsoft.durabletask.largepayloads.LargePayloadPurge/SetLargePayloadAutoPurge";

    const string Category = "Microsoft.DurableTask.Worker.Grpc";

    static readonly MethodInfo ConnectAsyncMethod = typeof(GrpcDurableTaskWorker)
        .GetNestedType("Processor", BindingFlags.NonPublic)!
        .GetMethod("ConnectAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConnectAsync_WhenOptedIn_SendsTheConfiguredValueBeforeRequestingWorkItems(bool enabled)
    {
        // Arrange
        GrpcDurableTaskWorkerOptions grpcOptions = new() { LargePayloadAutoPurgeEnabled = enabled };
        RecordingCallInvoker invoker = new();

        // Act
        await InvokeConnectAsync(CreateProcessor(grpcOptions, invoker, out _));

        // Assert - exactly one call, carrying the configured value, and ordered before GetWorkItems. The order
        // is the contract: a worker that started draining before announcing could act on a stale setting.
        invoker.AutoPurgeRequests.Should().ContainSingle().Which.Enabled.Should().Be(enabled);
        invoker.CallLog.Should().Equal("Hello", "SetLargePayloadAutoPurge", "GetWorkItems");
    }

    [Fact]
    public async Task ConnectAsync_WhenNoOpinion_SendsNothingAtAll()
    {
        // Arrange - the default for every worker that has not configured externalized payloads. Sending a
        // default false here would silently disable auto-purge for a task hub another worker opted in.
        GrpcDurableTaskWorkerOptions grpcOptions = new();
        RecordingCallInvoker invoker = new();

        // Act
        await InvokeConnectAsync(CreateProcessor(grpcOptions, invoker, out _));

        // Assert - the positive control for this is the theory above, which drives the same wiring and does see
        // the call; without it an always-broken RPC path would pass here.
        invoker.AutoPurgeRequests.Should().BeEmpty();
        invoker.CallLog.Should().Equal("Hello", "GetWorkItems");
    }

    [Fact]
    public async Task ConnectAsync_WhenServiceIsUnimplemented_StillOpensTheWorkItemStream()
    {
        // Arrange - a backend that is not the Durable Task Scheduler, or one mid-rollout.
        GrpcDurableTaskWorkerOptions grpcOptions = new() { LargePayloadAutoPurgeEnabled = true };
        RecordingCallInvoker invoker = new(
            new RpcException(new Status(StatusCode.Unimplemented, "unknown service")));
        TestLogProvider logProvider = new(new NullOutput());

        // Act
        await InvokeConnectAsync(CreateProcessor(grpcOptions, invoker, out _, logProvider));

        // Assert - orchestration execution proceeds; only the cleanup feature is skipped, and it says so.
        invoker.CallLog.Should().Equal("Hello", "SetLargePayloadAutoPurge", "GetWorkItems");
        logProvider.TryGetLogs(Category, out IReadOnlyCollection<LogEntry>? logs).Should().BeTrue();
        logs!.Should().Contain(log => log.Message.Contains("does not implement the large-payload auto-purge setting RPC"));
    }

    [Fact]
    public async Task ConnectAsync_WhenServiceFailsTransiently_StillOpensTheWorkItemStream()
    {
        // Arrange - the service exists but is unhappy. This is the branch that would otherwise reach
        // ConnectAsync's caller and increment the channel-poisoned counters.
        GrpcDurableTaskWorkerOptions grpcOptions = new() { LargePayloadAutoPurgeEnabled = true };
        RecordingCallInvoker invoker = new(
            new RpcException(new Status(StatusCode.Unavailable, "backend busy")));
        TestLogProvider logProvider = new(new NullOutput());

        // Act
        await InvokeConnectAsync(CreateProcessor(grpcOptions, invoker, out _, logProvider));

        // Assert
        invoker.CallLog.Should().Equal("Hello", "SetLargePayloadAutoPurge", "GetWorkItems");
        logProvider.TryGetLogs(Category, out IReadOnlyCollection<LogEntry>? logs).Should().BeTrue();
        logs!.Should().Contain(log => log.Message.Contains("Failed to announce the large-payload auto-purge setting"));
    }

    [Fact]
    public async Task ConnectAsync_WhenCancelledDuringTheCall_DoesNotOpenTheWorkItemStream()
    {
        // Arrange - shutdown. Swallowing here would let the processor go on to open a work-item stream it is
        // about to abandon, so cancellation is the one failure deliberately left to propagate.
        GrpcDurableTaskWorkerOptions grpcOptions = new() { LargePayloadAutoPurgeEnabled = true };
        using CancellationTokenSource cts = new();
        RecordingCallInvoker invoker = new(
            new RpcException(new Status(StatusCode.Cancelled, "shutting down")),
            onAutoPurgeCall: cts.Cancel);

        // Act
        Func<Task> act = () => InvokeConnectAsync(CreateProcessor(grpcOptions, invoker, out _), cts.Token);

        // Assert
        await act.Should().ThrowAsync<RpcException>();
        invoker.CallLog.Should().Equal("Hello", "SetLargePayloadAutoPurge");
    }

    static object CreateProcessor(
        GrpcDurableTaskWorkerOptions grpcOptions,
        CallInvoker invoker,
        out GrpcDurableTaskWorker worker,
        TestLogProvider? logProvider = null)
    {
        DurableTaskWorkerOptions workerOptions = new() { Logging = { UseLegacyCategories = false } };
        ILoggerFactory loggerFactory = logProvider is null
            ? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance
            : new SimpleLoggerFactory(logProvider);

        worker = new GrpcDurableTaskWorker(
            name: "Test",
            factory: Mock.Of<IDurableTaskFactory>(),
            grpcOptions: new OptionsMonitorStub<GrpcDurableTaskWorkerOptions>(grpcOptions),
            workerOptions: new OptionsMonitorStub<DurableTaskWorkerOptions>(workerOptions),
            services: Mock.Of<IServiceProvider>(),
            loggerFactory: loggerFactory,
            orchestrationFilter: null,
            exceptionPropertiesProvider: null,
            workItemFiltersMonitor: null);

        // Both clients share one CallInvoker, exactly as GrpcDurableTaskWorker builds them, so the ordering
        // assertions observe a single real call sequence rather than two independent fakes.
        System.Type processorType = typeof(GrpcDurableTaskWorker).GetNestedType("Processor", BindingFlags.NonPublic)!;
        return Activator.CreateInstance(
            processorType,
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            args: new object?[]
            {
                worker,
                new P.TaskHubSidecarService.TaskHubSidecarServiceClient(invoker),
                new LP.LargePayloadPurge.LargePayloadPurgeClient(invoker),
                null,
                null,
            },
            culture: null)!;
    }

    static async Task InvokeConnectAsync(object processor, CancellationToken cancellationToken = default)
    {
        await (Task)ConnectAsyncMethod.Invoke(processor, new object?[] { cancellationToken })!;
    }

    /// <summary>
    /// Records the ordered method names ConnectAsync drives and the auto-purge requests it sends, optionally
    /// faulting the auto-purge call. Hello and GetWorkItems always succeed so a failure of the auto-purge call
    /// is the only variable in play.
    /// </summary>
    sealed class RecordingCallInvoker(RpcException? autoPurgeError = null, Action? onAutoPurgeCall = null)
        : CallInvoker
    {
        readonly List<string> callLog = [];

        public IReadOnlyList<string> CallLog => this.callLog;

        public List<LP.SetLargePayloadAutoPurgeRequest> AutoPurgeRequests { get; } = [];

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            if (method.FullName == "/TaskHubSidecarService/Hello")
            {
                this.callLog.Add("Hello");
                return Completed<TResponse>(Task.FromResult((TResponse)(object)new Empty()));
            }

            if (method.FullName == SetAutoPurgeMethod)
            {
                this.callLog.Add("SetLargePayloadAutoPurge");
                this.AutoPurgeRequests.Add((LP.SetLargePayloadAutoPurgeRequest)(object)request!);
                onAutoPurgeCall?.Invoke();

                return autoPurgeError is null
                    ? Completed<TResponse>(
                        Task.FromResult((TResponse)(object)new LP.SetLargePayloadAutoPurgeResponse()))
                    : Completed<TResponse>(Task.FromException<TResponse>(autoPurgeError), autoPurgeError);
            }

            throw new NotSupportedException($"Unexpected unary method {method.FullName}.");
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            if (method.FullName == "/TaskHubSidecarService/GetWorkItems")
            {
                this.callLog.Add("GetWorkItems");
                return new AsyncServerStreamingCall<TResponse>(
                    Mock.Of<IAsyncStreamReader<TResponse>>(),
                    Task.FromResult(new Metadata()),
                    () => Status.DefaultSuccess,
                    () => new Metadata(),
                    () => { });
            }

            throw new NotSupportedException($"Unexpected server-streaming method {method.FullName}.");
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

        static AsyncUnaryCall<TResponse> Completed<TResponse>(Task<TResponse> response, RpcException? error = null)
            => new(
                response,
                Task.FromResult(new Metadata()),
                () => error?.Status ?? Status.DefaultSuccess,
                () => new Metadata(),
                () => { });
    }

    sealed class OptionsMonitorStub<T>(T value) : IOptionsMonitor<T>
        where T : class
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    sealed class SimpleLoggerFactory(ILoggerProvider provider) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider loggerProvider)
        {
        }

        public ILogger CreateLogger(string categoryName) => provider.CreateLogger(categoryName);

        public void Dispose()
        {
        }
    }

    sealed class NullOutput : ITestOutputHelper
    {
        public void WriteLine(string message)
        {
        }

        public void WriteLine(string format, params object[] args)
        {
        }
    }
}
