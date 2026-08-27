// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using System.Text;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.DurableTask.Client.Grpc.Internal;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DurableTask.Client.Grpc.Tests;

/// <summary>
/// Verifies that <see cref="GrpcDurableTaskClientOptions.Interceptors"/> is honored on every transport
/// path the client supports, that interception wraps <em>outside</em> <see cref="ChannelRecreatingCallInvoker"/>
/// so the wrapper's internal channel swaps stay transparent, and that registering no interceptors leaves
/// the transport invoker completely untouched.
/// </summary>
public class GrpcDurableTaskClientInterceptorsTests
{
    static readonly MethodInfo GetCallInvokerMethod = typeof(GrpcDurableTaskClient)
        .GetMethod("GetCallInvoker", BindingFlags.Static | BindingFlags.NonPublic)!;
    static readonly MethodInfo GetCallInvokerCoreMethod = typeof(GrpcDurableTaskClient)
        .GetMethod("GetCallInvokerCore", BindingFlags.Static | BindingFlags.NonPublic)!;

    [Fact]
    public void Interceptors_DefaultsToEmptyList()
    {
        // Arrange
        GrpcDurableTaskClientOptions options = new();

        // Act
        IList<Interceptor> interceptors = options.Interceptors;

        // Assert
        interceptors.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task GetCallInvoker_ChannelPath_AppliesInterceptors()
    {
        // Arrange
        GrpcChannel channel = GrpcChannel.ForAddress("http://localhost:5201");
        List<string> log = new();
        GrpcDurableTaskClientOptions options = new() { Channel = channel };
        options.Interceptors.Add(new RecordingInterceptor("only", log, passThrough: false));

        try
        {
            // Act
            (AsyncDisposable disposable, CallInvoker callInvoker) = InvokeGetCallInvoker(options);
            CallProbe.Invoke(callInvoker);

            // Assert
            log.Should().Equal("only");
            await disposable.DisposeAsync();
        }
        finally
        {
            channel.Dispose();
        }
    }

    [Fact]
    public async Task GetCallInvoker_AddressPath_AppliesInterceptors()
    {
        // Arrange
        List<string> log = new();
        GrpcDurableTaskClientOptions options = new() { Address = "http://localhost:5202" };
        options.Interceptors.Add(new RecordingInterceptor("only", log, passThrough: false));

        // Act
        (AsyncDisposable disposable, CallInvoker callInvoker) = InvokeGetCallInvoker(options);
        CallProbe.Invoke(callInvoker);

        // Assert
        log.Should().Equal("only");
        await disposable.DisposeAsync();
    }

    [Fact]
    public async Task GetCallInvoker_ExternalCallInvokerPath_AppliesInterceptors()
    {
        // Arrange
        StubCallInvoker external = new();
        List<string> log = new();
        GrpcDurableTaskClientOptions options = new() { CallInvoker = external };
        options.Interceptors.Add(new RecordingInterceptor("only", log, passThrough: true));

        // Act
        (AsyncDisposable disposable, CallInvoker callInvoker) = InvokeGetCallInvoker(options);
        CallProbe.Invoke(callInvoker);

        // Assert
        log.Should().Equal("only");
        external.CallCount.Should().Be(1);
        await disposable.DisposeAsync();
    }

    [Fact]
    public async Task GetCallInvoker_WithRecreator_AppliesInterceptorsOutsideRecreatingInvoker()
    {
        // Arrange: recreation stays enabled, so the core invoker is a ChannelRecreatingCallInvoker.
        // Interception must wrap outside it, otherwise the wrapper's internal channel swaps would replace
        // the intercepted invoker and drop the interceptors.
        GrpcChannel channel = GrpcChannel.ForAddress("http://localhost:5203");
        List<string> log = new();
        GrpcDurableTaskClientOptions options = new() { Channel = channel };
        options.SetChannelRecreator((existing, ct) => Task.FromResult(existing));
        options.Interceptors.Add(new RecordingInterceptor("only", log, passThrough: false));

        try
        {
            // Act
            (AsyncDisposable coreDisposable, CallInvoker coreInvoker) = InvokeGetCallInvokerCore(options);
            (AsyncDisposable disposable, CallInvoker callInvoker) = InvokeGetCallInvoker(options);
            CallProbe.Invoke(callInvoker);

            // Assert
            coreInvoker.Should().BeOfType<ChannelRecreatingCallInvoker>();
            callInvoker.Should().NotBeOfType<ChannelRecreatingCallInvoker>();
            log.Should().Equal("only");

            await coreDisposable.DisposeAsync();
            await disposable.DisposeAsync();
        }
        finally
        {
            channel.Dispose();
        }
    }

    [Fact]
    public async Task GetCallInvoker_NoInterceptors_ReturnsTransportInvokerUnchanged()
    {
        // Arrange: an externally-supplied invoker is handed back verbatim by the core builder, so it is
        // the one path where the purely-additive invariant can be asserted by reference.
        StubCallInvoker external = new();
        GrpcDurableTaskClientOptions options = new() { CallInvoker = external };

        // Act
        (AsyncDisposable disposable, CallInvoker callInvoker) = InvokeGetCallInvoker(options);

        // Assert
        callInvoker.Should().BeSameAs(external);
        await disposable.DisposeAsync();
    }

    [Fact]
    public async Task GetCallInvoker_NoInterceptors_WithRecreator_ReturnsRecreatingInvokerUnwrapped()
    {
        // Arrange
        GrpcChannel channel = GrpcChannel.ForAddress("http://localhost:5204");
        GrpcDurableTaskClientOptions options = new() { Channel = channel };
        options.SetChannelRecreator((existing, ct) => Task.FromResult(existing));

        try
        {
            // Act
            (AsyncDisposable disposable, CallInvoker callInvoker) = InvokeGetCallInvoker(options);

            // Assert
            callInvoker.Should().BeOfType<ChannelRecreatingCallInvoker>();
            await disposable.DisposeAsync();
        }
        finally
        {
            channel.Dispose();
        }
    }

    [Fact]
    public async Task GetCallInvoker_MultipleInterceptors_RunsInListOrder()
    {
        // Arrange: the documented contract is that the first interceptor added is the outermost, so it
        // observes the outgoing call before every interceptor added after it.
        StubCallInvoker external = new();
        List<string> log = new();
        GrpcDurableTaskClientOptions options = new() { CallInvoker = external };
        options.Interceptors.Add(new RecordingInterceptor("first", log, passThrough: true));
        options.Interceptors.Add(new RecordingInterceptor("second", log, passThrough: true));

        // Act
        (AsyncDisposable disposable, CallInvoker callInvoker) = InvokeGetCallInvoker(options);
        CallProbe.Invoke(callInvoker);

        // Assert
        log.Should().Equal("first", "second");
        external.CallCount.Should().Be(1);
        await disposable.DisposeAsync();
    }

    static (AsyncDisposable Disposable, CallInvoker CallInvoker) InvokeGetCallInvoker(
        GrpcDurableTaskClientOptions options)
        => Invoke(GetCallInvokerMethod, options);

    static (AsyncDisposable Disposable, CallInvoker CallInvoker) InvokeGetCallInvokerCore(
        GrpcDurableTaskClientOptions options)
        => Invoke(GetCallInvokerCoreMethod, options);

    static (AsyncDisposable Disposable, CallInvoker CallInvoker) Invoke(
        MethodInfo method, GrpcDurableTaskClientOptions options)
    {
        object?[] args = { options, NullLogger.Instance, null };
        AsyncDisposable disposable = (AsyncDisposable)method.Invoke(null, args)!;
        return (disposable, (CallInvoker)args[2]!);
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
