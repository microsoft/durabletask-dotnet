// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Reflection;
using System.Text;
using Grpc.Core;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client.Grpc.Internal;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DurableTask.Client.Grpc.Tests;

public class GrpcDurableTaskClientChannelRecreationTests
{
    static readonly Marshaller<string> StringMarshaller = Marshallers.Create(
        value => Encoding.UTF8.GetBytes(value),
        bytes => Encoding.UTF8.GetString(bytes));
    static readonly Method<string, string> TestMethod = new(
        MethodType.Unary,
        "TestService",
        "TestMethod",
        StringMarshaller,
        StringMarshaller);
    static readonly MethodInfo GetCallInvokerMethod = typeof(GrpcDurableTaskClient)
        .GetMethod("GetCallInvoker", BindingFlags.Static | BindingFlags.NonPublic)!;
    static readonly MethodInfo ToStopwatchTicksMethod = typeof(ChannelRecreatingCallInvoker)
        .GetMethod("ToStopwatchTicks", BindingFlags.Static | BindingFlags.NonPublic)!;

    [Fact]
    public async Task GetCallInvoker_WithProvidedChannel_RecreatesTransportAfterUnaryFailure()
    {
        // Arrange
        CallbackHttpMessageHandler initialHandler = new((request, cancellationToken) =>
            Task.FromResult(CreateFailureResponse(StatusCode.Unavailable, "initial transport failure")));
        TaskCompletionSource recreatedTransportUsed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CallbackHttpMessageHandler recreatedHandler = new((request, cancellationToken) =>
        {
            recreatedTransportUsed.TrySetResult();
            return Task.FromResult(CreateFailureResponse(StatusCode.Unavailable, "recreated transport failure"));
        });

        GrpcChannel channel = CreateChannel("http://initial.client.test", initialHandler);
        GrpcChannel recreatedChannel = CreateChannel("http://recreated.client.test", recreatedHandler);
        GrpcDurableTaskClientOptions options = new()
        {
            Channel = channel,
        };
        options.Internal.ChannelRecreateFailureThreshold = 2;
        options.Internal.MinRecreateInterval = TimeSpan.Zero;

        TaskCompletionSource<GrpcChannel> recreateRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int recreatorCalls = 0;
        options.SetChannelRecreator((existingChannel, ct) =>
        {
            recreatorCalls++;
            recreateRequested.TrySetResult(existingChannel);
            return Task.FromResult(recreatedChannel);
        });

        try
        {
            // Act
            (AsyncDisposable disposable, CallInvoker callInvoker) = InvokeGetCallInvoker(options);

            try
            {
                callInvoker.Should().BeOfType<ChannelRecreatingCallInvoker>();
                GetOwnsChannel(callInvoker).Should().BeFalse();

                // Act
                await AssertRpcFailureAsync(callInvoker);
                await AssertRpcFailureAsync(callInvoker);
                await recreateRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await AssertRpcFailureAsync(callInvoker);
                await recreatedTransportUsed.Task.WaitAsync(TimeSpan.FromSeconds(5));

                // Assert
                initialHandler.CallCount.Should().Be(2);
                recreatedHandler.CallCount.Should().Be(1);
                recreatorCalls.Should().Be(1);
            }
            finally
            {
                await disposable.DisposeAsync();
            }
        }
        finally
        {
            await DisposeChannelAsync(channel);
            await DisposeChannelAsync(recreatedChannel);
        }
    }

    [Fact]
    public async Task GetCallInvoker_WithAddressAndRecreator_UsesWrapperThatOwnsCreatedChannel()
        {
            // Arrange
            GrpcDurableTaskClientOptions options = new()
            {
                Address = "http://owned.client.test",
            };
            options.SetChannelRecreator((existingChannel, ct) => Task.FromResult(existingChannel));

        // Act
        (AsyncDisposable disposable, CallInvoker callInvoker) = InvokeGetCallInvoker(options);

        try
        {
            // Assert
            callInvoker.Should().BeOfType<ChannelRecreatingCallInvoker>();
            GetOwnsChannel(callInvoker).Should().BeTrue();
        }
        finally
        {
            await disposable.DisposeAsync();
        }
    }

    [Fact]
    public async Task CreateRecreateCancellationSource_WhenDisposedDuringRecreateWindow_ReturnsCanceledTokenSource()
    {
        // Arrange
        GrpcChannel channel = GrpcChannel.ForAddress("http://disposed-race.client.test");
        GrpcDurableTaskClientOptions options = new()
        {
            Channel = channel,
        };
        options.SetChannelRecreator((existingChannel, ct) => Task.FromResult(existingChannel));

        try
        {
            (AsyncDisposable disposable, CallInvoker callInvoker) = InvokeGetCallInvoker(options);

            try
            {
                ChannelRecreatingCallInvoker wrapper = callInvoker.Should().BeOfType<ChannelRecreatingCallInvoker>().Subject;
                MethodInfo? method = typeof(ChannelRecreatingCallInvoker).GetMethod(
                    "CreateRecreateCancellationSource",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                method.Should().NotBeNull();

                SetDisposed(wrapper, 1);
                GetDisposalCancellationSource(wrapper).Dispose();

                // Act
                using CancellationTokenSource recreateCts =
                    (CancellationTokenSource)method!.Invoke(wrapper, Array.Empty<object>())!;

                // Assert
                recreateCts.IsCancellationRequested.Should().BeTrue();
            }
            finally
            {
                await disposable.DisposeAsync();
            }
        }
        finally
        {
            await DisposeChannelAsync(channel);
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    public void ToStopwatchTicks_NonPositiveInterval_ReturnsZero(long ticks, long expected)
    {
        // Arrange
        TimeSpan interval = TimeSpan.FromTicks(ticks);

        // Act
        long stopwatchTicks = InvokeToStopwatchTicks(interval);

        // Assert
        stopwatchTicks.Should().Be(expected);
    }

    [Fact]
    public void ToStopwatchTicks_VeryLargeInterval_SaturatesAtLongMaxValue()
    {
        // Arrange
        TimeSpan interval = TimeSpan.MaxValue;

        // Act
        long stopwatchTicks = InvokeToStopwatchTicks(interval);

        // Assert
        stopwatchTicks.Should().Be(long.MaxValue);
    }

    static (AsyncDisposable Disposable, CallInvoker CallInvoker) InvokeGetCallInvoker(GrpcDurableTaskClientOptions options)
    {
        object?[] args = { options, NullLogger.Instance, null };
        AsyncDisposable disposable = (AsyncDisposable)GetCallInvokerMethod.Invoke(null, args)!;
        CallInvoker callInvoker = (CallInvoker)args[2]!;
        return (disposable, callInvoker);
    }

    static bool GetOwnsChannel(CallInvoker callInvoker)
    {
        return (bool)typeof(ChannelRecreatingCallInvoker)
            .GetField("ownsChannel", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(callInvoker)!;
    }

    static CancellationTokenSource GetDisposalCancellationSource(CallInvoker callInvoker)
    {
        return (CancellationTokenSource)typeof(ChannelRecreatingCallInvoker)
            .GetField("disposalCts", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(callInvoker)!;
    }

    static void SetDisposed(CallInvoker callInvoker, int value)
    {
        typeof(ChannelRecreatingCallInvoker)
            .GetField("disposed", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(callInvoker, value);
    }

    static long InvokeToStopwatchTicks(TimeSpan interval)
    {
        return (long)ToStopwatchTicksMethod.Invoke(null, new object?[] { interval })!;
    }

    static async Task AssertRpcFailureAsync(CallInvoker callInvoker)
    {
        Func<Task> act = async () =>
        {
            using AsyncUnaryCall<string> call = callInvoker.AsyncUnaryCall(
                TestMethod,
                host: null,
                new CallOptions(deadline: DateTime.UtcNow.AddSeconds(1)),
                request: "ping");

            await call.ResponseAsync;
        };

        RpcException rpcException = (await act.Should().ThrowAsync<RpcException>()).Which;
        rpcException.StatusCode.Should().Be(StatusCode.Unavailable);
    }

    static GrpcChannel CreateChannel(string address, HttpMessageHandler handler)
    {
        return GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            HttpHandler = handler,
        });
    }

    static async ValueTask DisposeChannelAsync(GrpcChannel channel)
    {
        await channel.ShutdownAsync();
        channel.Dispose();
    }

    static HttpResponseMessage CreateFailureResponse(StatusCode statusCode, string detail)
    {
        HttpResponseMessage response = new(System.Net.HttpStatusCode.OK)
        {
            Version = new Version(2, 0),
            Content = new ByteArrayContent([]),
        };

        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/grpc");
        response.TrailingHeaders.Add("grpc-status", ((int)statusCode).ToString());
        response.TrailingHeaders.Add("grpc-message", detail);
        return response;
    }

    sealed class CallbackHttpMessageHandler : HttpMessageHandler
    {
        readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback;
        int callCount;

        public CallbackHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback)
        {
            this.callback = callback;
        }

        public int CallCount => Volatile.Read(ref this.callCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this.callCount);
            return this.callback(request, cancellationToken);
        }
    }
}
