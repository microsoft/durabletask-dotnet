// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using System.Text;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.DurableTask.Worker.Grpc.Internal;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

/// <summary>
/// Verifies that <see cref="GrpcDurableTaskWorkerOptions.Interceptors"/> is honored everywhere the worker
/// produces a <see cref="CallInvoker"/> — including the invokers rebuilt on both channel-recreate paths —
/// so extensions can install interceptors without clearing <c>Channel</c> and disabling channel recreation.
/// </summary>
public class GrpcDurableTaskWorkerInterceptorsTests
{
    static readonly MethodInfo GetCallInvokerMethod = typeof(GrpcDurableTaskWorker)
        .GetMethod("GetCallInvoker", BindingFlags.Instance | BindingFlags.NonPublic)!;
    static readonly MethodInfo TryRecreateChannelAsyncMethod = typeof(GrpcDurableTaskWorker)
        .GetMethod("TryRecreateChannelAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Fact]
    public void Interceptors_DefaultsToEmptyList()
    {
        // Arrange
        GrpcDurableTaskWorkerOptions options = new();

        // Act
        IList<Interceptor> interceptors = options.Interceptors;

        // Assert
        interceptors.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void GetCallInvoker_ChannelPath_AppliesInterceptors()
    {
        // Arrange
        GrpcChannel channel = GrpcChannel.ForAddress("http://localhost:5101");
        List<string> log = new();
        GrpcDurableTaskWorkerOptions grpcOptions = new() { Channel = channel };
        grpcOptions.Interceptors.Add(new RecordingInterceptor("only", log, passThrough: false));
        GrpcDurableTaskWorker worker = CreateWorker(grpcOptions);

        try
        {
            // Act
            InvokeGetCallInvoker(worker, out CallInvoker callInvoker, out string address);
            CallProbe.Invoke(callInvoker);

            // Assert
            log.Should().Equal("only");
            address.Should().Be(channel.Target);
        }
        finally
        {
            channel.Dispose();
        }
    }

    [Fact]
    public void GetCallInvoker_AddressPath_AppliesInterceptors()
    {
        // Arrange
        List<string> log = new();
        GrpcDurableTaskWorkerOptions grpcOptions = new() { Address = "http://localhost:5102" };
        grpcOptions.Interceptors.Add(new RecordingInterceptor("only", log, passThrough: false));
        GrpcDurableTaskWorker worker = CreateWorker(grpcOptions);

        // Act
        InvokeGetCallInvoker(worker, out CallInvoker callInvoker, out _);
        CallProbe.Invoke(callInvoker);

        // Assert
        log.Should().Equal("only");
    }

    [Fact]
    public void GetCallInvoker_ExternalCallInvokerPath_AppliesInterceptors()
    {
        // Arrange
        StubCallInvoker external = new();
        List<string> log = new();
        GrpcDurableTaskWorkerOptions grpcOptions = new() { CallInvoker = external };
        grpcOptions.Interceptors.Add(new RecordingInterceptor("only", log, passThrough: true));
        GrpcDurableTaskWorker worker = CreateWorker(grpcOptions);

        // Act
        InvokeGetCallInvoker(worker, out CallInvoker callInvoker, out _);
        CallProbe.Invoke(callInvoker);

        // Assert
        log.Should().Equal("only");
        external.CallCount.Should().Be(1);
    }

    [Fact]
    public void GetCallInvoker_NoInterceptors_ReturnsTransportInvokerUnchanged()
    {
        // Arrange: an externally-supplied invoker is handed back verbatim by the core builder, so it is
        // the one path where the purely-additive invariant can be asserted by reference.
        StubCallInvoker external = new();
        GrpcDurableTaskWorkerOptions grpcOptions = new() { CallInvoker = external };
        GrpcDurableTaskWorker worker = CreateWorker(grpcOptions);

        // Act
        InvokeGetCallInvoker(worker, out CallInvoker callInvoker, out _);

        // Assert
        callInvoker.Should().BeSameAs(external);
    }

    [Fact]
    public void GetCallInvoker_NoInterceptors_ChannelPath_ReturnsChannelInvokerUnwrapped()
    {
        // Arrange
        GrpcChannel channel = GrpcChannel.ForAddress("http://localhost:5103");
        GrpcDurableTaskWorkerOptions grpcOptions = new() { Channel = channel };
        GrpcDurableTaskWorker worker = CreateWorker(grpcOptions);

        try
        {
            // Act
            InvokeGetCallInvoker(worker, out CallInvoker callInvoker, out string address);

            // Assert
            callInvoker.Should().BeOfType(channel.CreateCallInvoker().GetType());
            address.Should().Be(channel.Target);
        }
        finally
        {
            channel.Dispose();
        }
    }

    [Fact]
    public void GetCallInvoker_MultipleInterceptors_RunsInListOrder()
    {
        // Arrange: the documented contract is that the first interceptor added is the outermost, so it
        // observes the outgoing call before every interceptor added after it.
        StubCallInvoker external = new();
        List<string> log = new();
        GrpcDurableTaskWorkerOptions grpcOptions = new() { CallInvoker = external };
        grpcOptions.Interceptors.Add(new RecordingInterceptor("first", log, passThrough: true));
        grpcOptions.Interceptors.Add(new RecordingInterceptor("second", log, passThrough: true));
        GrpcDurableTaskWorker worker = CreateWorker(grpcOptions);

        // Act
        InvokeGetCallInvoker(worker, out CallInvoker callInvoker, out _);
        CallProbe.Invoke(callInvoker);

        // Assert
        log.Should().Equal("first", "second");
        external.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task TryRecreateChannelAsync_RecreatorPath_InterceptsRebuiltInvoker()
    {
        // Arrange: this is the shape the AzureBlobPayloads extension used to break — a DTS-configured
        // Channel plus recreator. This path requires Channel to still be set, and the invoker built from
        // the replacement channel must still carry the configured interceptors.
        GrpcChannel currentChannel = GrpcChannel.ForAddress("http://localhost:5104");
        GrpcChannel recreatedChannel = GrpcChannel.ForAddress("http://localhost:5105");
        List<string> log = new();
        GrpcDurableTaskWorkerOptions grpcOptions = new() { Channel = currentChannel };
        grpcOptions.SetChannelRecreator((channel, ct) => Task.FromResult(recreatedChannel));
        grpcOptions.Interceptors.Add(new RecordingInterceptor("only", log, passThrough: false));
        GrpcDurableTaskWorker worker = CreateWorker(grpcOptions);

        try
        {
            // Act
            object result = await InvokeTryRecreateChannelAsync(worker, currentChannel);

            // Assert
            GetResultProperty<bool>(result, "Recreated").Should().BeTrue();
            GetResultProperty<GrpcChannel?>(result, "NewChannel").Should().BeSameAs(recreatedChannel);

            CallProbe.Invoke(GetResultProperty<CallInvoker>(result, "NewCallInvoker"));
            log.Should().Equal("only");
        }
        finally
        {
            currentChannel.Dispose();
            recreatedChannel.Dispose();
        }
    }

    [Fact]
    public async Task TryRecreateChannelAsync_WorkerOwnedPath_InterceptsRebuiltInvoker()
    {
        // Arrange: Address-only configuration takes the worker-owned rebuild path.
        List<string> log = new();
        GrpcDurableTaskWorkerOptions grpcOptions = new() { Address = "http://localhost:5106" };
        grpcOptions.Interceptors.Add(new RecordingInterceptor("only", log, passThrough: false));
        GrpcDurableTaskWorker worker = CreateWorker(grpcOptions);
        GrpcChannel currentChannel = GrpcChannel.ForAddress(grpcOptions.Address);

        try
        {
            // Act
            object result = await InvokeTryRecreateChannelAsync(worker, currentChannel);

            // Assert
            GetResultProperty<bool>(result, "Recreated").Should().BeTrue();

            CallProbe.Invoke(GetResultProperty<CallInvoker>(result, "NewCallInvoker"));
            log.Should().Equal("only");

            AsyncDisposable newDisposable = GetResultProperty<AsyncDisposable>(result, "NewWorkerOwnedDisposable");
            await newDisposable.DisposeAsync();
        }
        finally
        {
            currentChannel.Dispose();
        }
    }

    [Fact]
    public async Task TryRecreateChannelAsync_NoInterceptors_ReturnsChannelInvokerUnwrapped()
    {
        // Arrange
        GrpcChannel currentChannel = GrpcChannel.ForAddress("http://localhost:5107");
        GrpcChannel recreatedChannel = GrpcChannel.ForAddress("http://localhost:5108");
        GrpcDurableTaskWorkerOptions grpcOptions = new() { Channel = currentChannel };
        grpcOptions.SetChannelRecreator((channel, ct) => Task.FromResult(recreatedChannel));
        GrpcDurableTaskWorker worker = CreateWorker(grpcOptions);

        try
        {
            // Act
            object result = await InvokeTryRecreateChannelAsync(worker, currentChannel);

            // Assert
            GetResultProperty<CallInvoker>(result, "NewCallInvoker")
                .Should().BeOfType(recreatedChannel.CreateCallInvoker().GetType());
        }
        finally
        {
            currentChannel.Dispose();
            recreatedChannel.Dispose();
        }
    }

    [Fact]
    public async Task Interceptors_AddedAfterWorkerConstruction_NeverTakeEffect_EvenAfterChannelRecreate()
    {
        // Arrange: the interceptor chain is captured when the worker is constructed. Options instances are
        // cached per name by IOptionsMonitor, so a caller holding the same monitor can mutate the very list
        // the worker was configured from. Without a snapshot, the recreate path re-reads that list and the
        // late addition silently activates at the next externally-triggered recreate.
        //
        // The late interceptor is inserted at index 0 so it becomes the outermost link. That keeps the probe
        // network-free in both directions: whichever interceptor is outermost short-circuits the call. An
        // Add() to the end is the same defect — it mutates the same list the recreate path re-reads — but it
        // lands innermost, where the outer short-circuit would mask it and make the assertion vacuous.
        GrpcChannel currentChannel = GrpcChannel.ForAddress("http://localhost:5109");
        GrpcChannel recreatedChannel = GrpcChannel.ForAddress("http://localhost:5110");
        List<string> log = new();
        GrpcDurableTaskWorkerOptions grpcOptions = new() { Channel = currentChannel };
        grpcOptions.SetChannelRecreator((channel, ct) => Task.FromResult(recreatedChannel));
        grpcOptions.Interceptors.Add(new RecordingInterceptor("at-construction", log, passThrough: false));
        GrpcDurableTaskWorker worker = CreateWorker(grpcOptions);

        try
        {
            // Act: mutate the live options collection after the worker was built, then force a recreate.
            grpcOptions.Interceptors.Insert(0, new RecordingInterceptor("added-late", log, passThrough: false));
            object result = await InvokeTryRecreateChannelAsync(worker, currentChannel);

            CallProbe.Invoke(GetResultProperty<CallInvoker>(result, "NewCallInvoker"));

            // Assert: the rebuilt invoker still carries exactly the chain captured at construction.
            GetResultProperty<bool>(result, "Recreated").Should().BeTrue();
            log.Should().Equal("at-construction");
            log.Should().NotContain("added-late");
        }
        finally
        {
            currentChannel.Dispose();
            recreatedChannel.Dispose();
        }
    }

    [Fact]
    public async Task Interceptors_AddedAfterWorkerConstruction_NeverTakeEffect_OnWorkerOwnedRecreate()
    {
        // Arrange: same contract on the Address-only rebuild path, which re-reads the collection separately.
        List<string> log = new();
        GrpcDurableTaskWorkerOptions grpcOptions = new() { Address = "http://localhost:5111" };
        grpcOptions.Interceptors.Add(new RecordingInterceptor("at-construction", log, passThrough: false));
        GrpcDurableTaskWorker worker = CreateWorker(grpcOptions);
        GrpcChannel currentChannel = GrpcChannel.ForAddress(grpcOptions.Address);

        try
        {
            // Act
            grpcOptions.Interceptors.Insert(0, new RecordingInterceptor("added-late", log, passThrough: false));
            object result = await InvokeTryRecreateChannelAsync(worker, currentChannel);

            CallProbe.Invoke(GetResultProperty<CallInvoker>(result, "NewCallInvoker"));

            // Assert
            GetResultProperty<bool>(result, "Recreated").Should().BeTrue();
            log.Should().Equal("at-construction");

            AsyncDisposable newDisposable = GetResultProperty<AsyncDisposable>(result, "NewWorkerOwnedDisposable");
            await newDisposable.DisposeAsync();
        }
        finally
        {
            currentChannel.Dispose();
        }
    }

    [Fact]
    public void Interceptors_AddedAfterWorkerConstruction_DoNotAffectStartupInvoker()
    {
        // Arrange
        StubCallInvoker external = new();
        List<string> log = new();
        GrpcDurableTaskWorkerOptions grpcOptions = new() { CallInvoker = external };
        grpcOptions.Interceptors.Add(new RecordingInterceptor("at-construction", log, passThrough: true));
        GrpcDurableTaskWorker worker = CreateWorker(grpcOptions);

        // Act
        grpcOptions.Interceptors.Insert(0, new RecordingInterceptor("added-late", log, passThrough: true));
        InvokeGetCallInvoker(worker, out CallInvoker callInvoker, out _);
        CallProbe.Invoke(callInvoker);

        // Assert
        log.Should().Equal("at-construction");
        external.CallCount.Should().Be(1);
    }

    static void InvokeGetCallInvoker(GrpcDurableTaskWorker worker, out CallInvoker callInvoker, out string address)
    {
        object?[] args = { null, null };
        GetCallInvokerMethod.Invoke(worker, args);
        callInvoker = (CallInvoker)args[0]!;
        address = (string)args[1]!;
    }

    static async Task<object> InvokeTryRecreateChannelAsync(GrpcDurableTaskWorker worker, GrpcChannel currentChannel)
    {
        object?[] args = { CancellationToken.None, default(AsyncDisposable), currentChannel };
        Task task = (Task)TryRecreateChannelAsyncMethod.Invoke(worker, args)!;
        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    static T GetResultProperty<T>(object result, string propertyName)
        => (T)result.GetType().GetProperty(propertyName)!.GetValue(result)!;

    static GrpcDurableTaskWorker CreateWorker(GrpcDurableTaskWorkerOptions grpcOptions)
    {
        return new GrpcDurableTaskWorker(
            name: "Test",
            factory: Mock.Of<IDurableTaskFactory>(),
            grpcOptions: new OptionsMonitorStub<GrpcDurableTaskWorkerOptions>(grpcOptions),
            workerOptions: new OptionsMonitorStub<DurableTaskWorkerOptions>(new DurableTaskWorkerOptions()),
            services: Mock.Of<IServiceProvider>(),
            loggerFactory: NullLoggerFactory.Instance,
            orchestrationFilter: null,
            exceptionPropertiesProvider: null,
            workItemFiltersMonitor: null);
    }

    /// <summary>
    /// Drives a single unary call through an invoker without touching the network: the innermost
    /// participant always short-circuits.
    /// </summary>
    static class CallProbe
    {
        static readonly Marshaller<ProbeMessage> Marshaller = Marshallers.Create(
            m => Encoding.UTF8.GetBytes(m.Value), b => new ProbeMessage { Value = Encoding.UTF8.GetString(b) });

        static readonly Method<ProbeMessage, ProbeMessage> Method = new(
            MethodType.Unary, "Probe", "Probe", Marshaller, Marshaller);

        public static void Invoke(CallInvoker invoker)
            => invoker.AsyncUnaryCall(Method, null, default, new ProbeMessage()).Dispose();
    }

    sealed class ProbeMessage
    {
        public string Value { get; set; } = string.Empty;
    }

    sealed class RecordingInterceptor : Interceptor
    {
        readonly string tag;
        readonly IList<string> log;
        readonly bool passThrough;

        public RecordingInterceptor(string tag, IList<string> log, bool passThrough)
        {
            this.tag = tag;
            this.log = log;
            this.passThrough = passThrough;
        }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            this.log.Add(this.tag);
            return this.passThrough ? continuation(request, context) : StubCallInvoker.EmptyCall<TResponse>();
        }
    }

    sealed class StubCallInvoker : CallInvoker
    {
        int callCount;

        public int CallCount => Volatile.Read(ref this.callCount);

        public static AsyncUnaryCall<TResponse> EmptyCall<TResponse>()
            where TResponse : class
        {
            return new AsyncUnaryCall<TResponse>(
                Task.FromResult(Activator.CreateInstance<TResponse>()),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });
        }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            Interlocked.Increment(ref this.callCount);
            return EmptyCall<TResponse>();
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
