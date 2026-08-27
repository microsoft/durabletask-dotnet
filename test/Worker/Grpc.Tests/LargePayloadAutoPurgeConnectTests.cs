// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.DurableTask.Tests.Logging;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;
using P = Microsoft.DurableTask.Protobuf;
using LP = Microsoft.DurableTask.Protobuf.LargePayloads;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

/// <summary>
/// The auto-purge opt-in is announced with a dedicated <c>SetLargePayloadAutoPurge</c> RPC on a service only the
/// Durable Task Scheduler implements, sent once per connection before the work-item stream is requested. Three
/// properties matter and none is visible from the type system: the RPC must not be sent when the worker has no
/// opinion; a backend that does not implement the service must not be blocked by it; and any OTHER failure must
/// stop the connect attempt rather than opening the work-item stream, because while the backend still believes
/// auto-purge is off it hard-deletes payload metadata without tombstoning and the blob reference is lost for good.
/// </summary>
public class LargePayloadAutoPurgeConnectTests
{
    const string SetAutoPurgeMethod =
        "/microsoft.durabletask.largepayloads.LargePayloadPurge/SetLargePayloadAutoPurge";

    const string Category = "Microsoft.DurableTask.Worker.Grpc";

    static readonly MethodInfo ConnectAsyncMethod = typeof(GrpcDurableTaskWorker)
        .GetNestedType("Processor", BindingFlags.NonPublic)!
        .GetMethod("ConnectAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

    static readonly MethodInfo ProcessorExecuteAsyncMethod = typeof(GrpcDurableTaskWorker)
        .GetNestedType("Processor", BindingFlags.NonPublic)!
        .GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.Public)!;

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
        // Arrange - a backend that is not the Durable Task Scheduler, or one mid-rollout. This is the one
        // status that is safe to absorb: a backend without the service is not tombstoning payloads at all, so
        // proceeding cannot lose a blob reference the way proceeding against an unconfirmed DTS backend can.
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

    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.Internal)]
    [InlineData(StatusCode.DeadlineExceeded)]
    public async Task ConnectAsync_WhenTheAnnouncementFails_DoesNotOpenTheWorkItemStream(StatusCode statusCode)
    {
        // Arrange - the service exists but did not confirm the setting. Unimplemented is the only absorbed
        // status; everything else must stop the connect attempt. DeadlineExceeded is included because that is
        // how the finite deadline below surfaces, so the bound and the gate are proven to compose.
        GrpcDurableTaskWorkerOptions grpcOptions = new() { LargePayloadAutoPurgeEnabled = true };
        RecordingCallInvoker invoker = new(new RpcException(new Status(statusCode, "no confirmation")));

        // Act
        Func<Task> act = () => InvokeConnectAsync(CreateProcessor(grpcOptions, invoker, out _));

        // Assert - the failure reaches the caller, and no work-item stream was opened. The positive control is
        // the opted-in theory above: the same wiring does reach GetWorkItems when the announcement succeeds.
        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(statusCode);
        invoker.CallLog.Should().Equal("Hello", "SetLargePayloadAutoPurge");
    }

    [Fact]
    public async Task ProcessorExecuteAsync_WhenTheAnnouncementFails_RetriesAndNeverOpensTheWorkItemStream()
    {
        // Arrange - proves the escaping exception lands in the real retry/backoff path rather than merely
        // escaping ConnectAsync. A threshold of 1 makes the first failure request a channel recreate, which is
        // the same treatment any other unconfirmed connection setup gets.
        GrpcDurableTaskWorkerOptions grpcOptions = new() { LargePayloadAutoPurgeEnabled = true };
        grpcOptions.Internal.ChannelRecreateFailureThreshold = 1;
        grpcOptions.Internal.ReconnectBackoffBase = TimeSpan.Zero;
        grpcOptions.Internal.ReconnectBackoffCap = TimeSpan.Zero;
        RecordingCallInvoker invoker = new(
            new RpcException(new Status(StatusCode.Unavailable, "no confirmation")));
        TestLogProvider logProvider = new(new NullOutput());

        // Act
        ProcessorExitReason reason = await InvokeProcessorExecuteAsync(
            CreateProcessor(grpcOptions, invoker, out _, logProvider), CancellationToken.None);

        // Assert
        reason.Should().Be(ProcessorExitReason.ChannelRecreateRequested);
        invoker.CallLog.Should().Equal("Hello", "SetLargePayloadAutoPurge");
        logProvider.TryGetLogs(Category, out IReadOnlyCollection<LogEntry>? logs).Should().BeTrue();
        logs!.Should().Contain(log => log.Message.Contains("Recreating gRPC channel to backend"));

        // The absorbed-failure log must not appear for a status that is not absorbed, or the single failure
        // would be reported twice - once here and once by ExecuteAsync's own handler.
        logs.Should().NotContain(log => log.Message.Contains("does not implement the large-payload auto-purge setting RPC"));
    }

    [Fact]
    public async Task ConnectAsync_BoundsTheAnnouncementWithTheConnectionSetupDeadline()
    {
        // Arrange - a half-open channel can leave a unary call pending forever, which without a deadline would
        // park the worker in connect with no work being taken and no timeout to break it.
        GrpcDurableTaskWorkerOptions grpcOptions = new() { LargePayloadAutoPurgeEnabled = true };
        grpcOptions.SetHelloDeadline(TimeSpan.FromSeconds(30));
        RecordingCallInvoker invoker = new();
        DateTime before = DateTime.UtcNow;

        // Act
        await InvokeConnectAsync(CreateProcessor(grpcOptions, invoker, out _));

        // Assert - a fake invoker cannot enforce a deadline, so what is asserted is the part that is actually
        // this code's responsibility: that a finite deadline is attached to the call at all, and that it is a
        // fresh one rather than whatever remained of Hello's. The DeadlineExceeded case in the theory above
        // covers the other half - that a call which does time out stops the connect attempt.
        invoker.AutoPurgeDeadline.Should().NotBeNull();
        invoker.AutoPurgeDeadline!.Value.Should().BeOnOrAfter(before.AddSeconds(30))
            .And.BeBefore(DateTime.UtcNow.AddSeconds(31));

        // Each setup RPC gets the full interval, so the later call's deadline is strictly later than the
        // earlier one's. A single shared absolute deadline would make these equal.
        invoker.HelloDeadline.Should().NotBeNull();
        invoker.AutoPurgeDeadline!.Value.Should().BeOnOrAfter(invoker.HelloDeadline!.Value);
    }

    [Fact]
    public async Task ConnectAsync_WhenTheDeadlineIsDisabled_SendsNoDeadline()
    {
        // Arrange - positive control for the assertion above: the deadline is genuinely driven by the option
        // rather than being unconditionally present.
        GrpcDurableTaskWorkerOptions grpcOptions = new() { LargePayloadAutoPurgeEnabled = true };
        grpcOptions.SetHelloDeadline(TimeSpan.Zero);
        RecordingCallInvoker invoker = new();

        // Act
        await InvokeConnectAsync(CreateProcessor(grpcOptions, invoker, out _));

        // Assert
        invoker.AutoPurgeDeadline.Should().BeNull();
        invoker.CallLog.Should().Equal("Hello", "SetLargePayloadAutoPurge", "GetWorkItems");
    }

    [Fact]
    public async Task ProcessorExecuteAsync_WhenTheAnnouncementRecovers_OpensTheStreamOnlyAfterItSucceeds()
    {
        // Arrange - the failure is not permanent, so the worker must resume taking work once the setting is
        // confirmed rather than staying wedged. The first attempt fails, the second succeeds.
        GrpcDurableTaskWorkerOptions grpcOptions = new() { LargePayloadAutoPurgeEnabled = true };
        grpcOptions.Internal.ChannelRecreateFailureThreshold = 0;
        grpcOptions.Internal.ReconnectBackoffBase = TimeSpan.Zero;
        grpcOptions.Internal.ReconnectBackoffCap = TimeSpan.Zero;
        using CancellationTokenSource cts = new();
        RecordingCallInvoker invoker = new(
            new RpcException(new Status(StatusCode.Unavailable, "no confirmation")),
            failAutoPurgeTimes: 1,
            onWorkItemsRequested: cts.Cancel);

        // Act
        await InvokeProcessorExecuteAsync(CreateProcessor(grpcOptions, invoker, out _), cts.Token);

        // Assert - GetWorkItems appears exactly once, and only after the second (successful) announcement.
        invoker.CallLog.Should().Equal(
            "Hello", "SetLargePayloadAutoPurge", "Hello", "SetLargePayloadAutoPurge", "GetWorkItems");
    }

    [Fact]
    public async Task ConnectAsync_WhenCancelledDuringTheCall_DoesNotOpenTheWorkItemStream()
    {
        // Arrange - shutdown. Cancellation propagates like any other non-Unimplemented failure, so the
        // processor exits instead of opening a work-item stream it is about to abandon.
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

    static async Task<ProcessorExitReason> InvokeProcessorExecuteAsync(
        object processor, CancellationToken cancellationToken)
    {
        Task task = (Task)ProcessorExecuteAsyncMethod.Invoke(processor, new object?[] { cancellationToken })!;
        await task;
        return (ProcessorExitReason)task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    /// <summary>
    /// Records the ordered method names ConnectAsync drives, the auto-purge requests it sends and the deadlines
    /// it attaches, optionally faulting the auto-purge call for a bounded number of attempts. Hello and
    /// GetWorkItems always succeed so a failure of the auto-purge call is the only variable in play.
    /// </summary>
    sealed class RecordingCallInvoker(
        RpcException? autoPurgeError = null,
        Action? onAutoPurgeCall = null,
        int failAutoPurgeTimes = int.MaxValue,
        Action? onWorkItemsRequested = null)
        : CallInvoker
    {
        readonly List<string> callLog = [];
        int autoPurgeCallCount;

        public IReadOnlyList<string> CallLog => this.callLog;

        public List<LP.SetLargePayloadAutoPurgeRequest> AutoPurgeRequests { get; } = [];

        public DateTime? AutoPurgeDeadline { get; private set; }

        public DateTime? HelloDeadline { get; private set; }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            if (method.FullName == "/TaskHubSidecarService/Hello")
            {
                this.callLog.Add("Hello");
                this.HelloDeadline = options.Deadline;
                return Completed<TResponse>(Task.FromResult((TResponse)(object)new Empty()));
            }

            if (method.FullName == SetAutoPurgeMethod)
            {
                this.callLog.Add("SetLargePayloadAutoPurge");
                this.AutoPurgeDeadline = options.Deadline;
                this.AutoPurgeRequests.Add((LP.SetLargePayloadAutoPurgeRequest)(object)request!);
                onAutoPurgeCall?.Invoke();

                bool shouldFail = autoPurgeError is not null && this.autoPurgeCallCount < failAutoPurgeTimes;
                this.autoPurgeCallCount++;

                return shouldFail
                    ? Completed<TResponse>(Task.FromException<TResponse>(autoPurgeError!), autoPurgeError)
                    : Completed<TResponse>(
                        Task.FromResult((TResponse)(object)new LP.SetLargePayloadAutoPurgeResponse()));
            }

            throw new NotSupportedException($"Unexpected unary method {method.FullName}.");
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            if (method.FullName == "/TaskHubSidecarService/GetWorkItems")
            {
                this.callLog.Add("GetWorkItems");
                onWorkItemsRequested?.Invoke();
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
