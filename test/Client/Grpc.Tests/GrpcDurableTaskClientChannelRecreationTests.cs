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
    static readonly Method<string, string> WaitForInstanceCompletionMethod = new(
        MethodType.Unary,
        "TaskHubSidecarService",
        "WaitForInstanceCompletion",
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
    public async Task AsyncUnaryCall_SuccessAfterFailure_ResetsConsecutiveFailureCountAndSuppressesRecreate()
    {
        // Arrange: threshold of 2 consecutive failures, but a success is interleaved so the counter
        // should reset and the recreator should never be invoked.
        int callIndex = 0;
        CallbackHttpMessageHandler handler = new((request, cancellationToken) =>
        {
            int index = Interlocked.Increment(ref callIndex);
            return Task.FromResult(index == 2
                ? CreateSuccessResponse("pong")
                : CreateFailureResponse(StatusCode.Unavailable, "transient failure"));
        });

        GrpcChannel channel = CreateChannel("http://success-reset.client.test", handler);
        GrpcDurableTaskClientOptions options = new() { Channel = channel };
        options.Internal.ChannelRecreateFailureThreshold = 2;
        options.Internal.MinRecreateInterval = TimeSpan.Zero;

        int recreatorCalls = 0;
        options.SetChannelRecreator((existingChannel, ct) =>
        {
            Interlocked.Increment(ref recreatorCalls);
            return Task.FromResult(existingChannel);
        });

        try
        {
            (AsyncDisposable disposable, CallInvoker callInvoker) = InvokeGetCallInvoker(options);
            try
            {
                // Act: failure, success, failure -- the success in the middle must reset the streak so
                // the trailing failure alone never reaches the threshold of 2.
                await AssertRpcFailureAsync(callInvoker);
                await AssertRpcSuccessAsync(callInvoker);
                await AssertRpcFailureAsync(callInvoker);

                await Task.Delay(TimeSpan.FromMilliseconds(200));

                // Assert
                GetConsecutiveFailures(callInvoker).Should().Be(1);
                recreatorCalls.Should().Be(0);
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

    [Fact]
    public async Task AsyncUnaryCall_DeadlineExceededOnAllowedLongPollMethod_DoesNotCountTowardThreshold()
    {
        // Arrange: WaitForInstanceCompletion is long-poll and DeadlineExceeded is expected behavior
        // there, so repeated timeouts on it must never count toward the recreate threshold.
        CallbackHttpMessageHandler handler = new((request, cancellationToken) =>
            Task.FromResult(CreateFailureResponse(StatusCode.DeadlineExceeded, "long-poll wait elapsed")));

        GrpcChannel channel = CreateChannel("http://deadline-allowed.client.test", handler);
        GrpcDurableTaskClientOptions options = new() { Channel = channel };
        options.Internal.ChannelRecreateFailureThreshold = 1;
        options.Internal.MinRecreateInterval = TimeSpan.Zero;

        int recreatorCalls = 0;
        options.SetChannelRecreator((existingChannel, ct) =>
        {
            Interlocked.Increment(ref recreatorCalls);
            return Task.FromResult(existingChannel);
        });

        try
        {
            (AsyncDisposable disposable, CallInvoker callInvoker) = InvokeGetCallInvoker(options);
            try
            {
                // Act
                await AssertRpcFailureAsync(
                    callInvoker, WaitForInstanceCompletionMethod, StatusCode.DeadlineExceeded);
                await AssertRpcFailureAsync(
                    callInvoker, WaitForInstanceCompletionMethod, StatusCode.DeadlineExceeded);

                await Task.Delay(TimeSpan.FromMilliseconds(200));

                // Assert
                GetConsecutiveFailures(callInvoker).Should().Be(0);
                recreatorCalls.Should().Be(0);
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

    [Fact]
    public async Task AsyncUnaryCall_DeadlineExceededOnRegularMethod_CountsTowardThresholdAndTriggersRecreate()
    {
        // Arrange: same DeadlineExceeded status as above, but on a method that is NOT in the long-poll
        // allow-list, so it must count toward the threshold and trigger a recreate.
        CallbackHttpMessageHandler handler = new((request, cancellationToken) =>
            Task.FromResult(CreateFailureResponse(StatusCode.DeadlineExceeded, "unexpected timeout")));

        GrpcChannel channel = CreateChannel("http://deadline-counts.client.test", handler);
        GrpcDurableTaskClientOptions options = new() { Channel = channel };
        options.Internal.ChannelRecreateFailureThreshold = 1;
        options.Internal.MinRecreateInterval = TimeSpan.Zero;

        TaskCompletionSource<GrpcChannel> recreateRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        options.SetChannelRecreator((existingChannel, ct) =>
        {
            recreateRequested.TrySetResult(existingChannel);
            return Task.FromResult(existingChannel);
        });

        try
        {
            (AsyncDisposable disposable, CallInvoker callInvoker) = InvokeGetCallInvoker(options);
            try
            {
                // Act
                await AssertRpcFailureAsync(callInvoker, TestMethod, StatusCode.DeadlineExceeded);
                GrpcChannel recreatorChannel = await recreateRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));

                // Assert
                recreatorChannel.Should().BeSameAs(channel);
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

    [Fact]
    public async Task AsyncUnaryCall_MultipleCalls_ReuseSameCachedOutcomeDelegateInstance()
    {
        // Arrange/Act: the outcome-observation delegate must be created exactly once per invoker
        // instance (in the constructor) and reused for every call, rather than allocated per RPC.
        GrpcChannel channel = GrpcChannel.ForAddress("http://cached-delegate.client.test");
        GrpcDurableTaskClientOptions options = new() { Channel = channel };
        options.SetChannelRecreator((existingChannel, ct) => Task.FromResult(existingChannel));

        try
        {
            (AsyncDisposable disposable, CallInvoker callInvoker) = InvokeGetCallInvoker(options);
            try
            {
                object? delegateBeforeCalls = GetOnUnaryCallCompletedDelegate(callInvoker);
                delegateBeforeCalls.Should().NotBeNull();

                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        using AsyncUnaryCall<string> call = callInvoker.AsyncUnaryCall(
                            TestMethod, host: null, new CallOptions(), request: "ping");
                        await call.ResponseAsync;
                    }
                    catch
                    {
                        // The call itself fails fast against a fake address; only the delegate identity
                        // across calls is under test here.
                    }
                }

                // Assert: still the exact same delegate instance (a readonly field set once in the
                // constructor), proving no per-call delegate is allocated.
                object? delegateAfterCalls = GetOnUnaryCallCompletedDelegate(callInvoker);
                delegateAfterCalls.Should().BeSameAs(delegateBeforeCalls);
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

    static int GetConsecutiveFailures(CallInvoker callInvoker)
    {
        return (int)typeof(ChannelRecreatingCallInvoker)
            .GetField("consecutiveFailures", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(callInvoker)!;
    }

    static object GetOnUnaryCallCompletedDelegate(CallInvoker callInvoker)
    {
        return typeof(ChannelRecreatingCallInvoker)
            .GetField("onUnaryCallCompleted", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(callInvoker)!;
    }

    static long InvokeToStopwatchTicks(TimeSpan interval)
    {
        return (long)ToStopwatchTicksMethod.Invoke(null, new object?[] { interval })!;
    }

    static async Task AssertRpcFailureAsync(CallInvoker callInvoker) =>
        await AssertRpcFailureAsync(callInvoker, TestMethod, StatusCode.Unavailable);

    static async Task AssertRpcFailureAsync(CallInvoker callInvoker, Method<string, string> method, StatusCode expectedStatus)
    {
        Func<Task> act = async () =>
        {
            using AsyncUnaryCall<string> call = callInvoker.AsyncUnaryCall(
                method,
                host: null,
                new CallOptions(deadline: DateTime.UtcNow.AddSeconds(1)),
                request: "ping");

            await call.ResponseAsync;
        };

        RpcException rpcException = (await act.Should().ThrowAsync<RpcException>()).Which;
        rpcException.StatusCode.Should().Be(expectedStatus);
    }

    static async Task AssertRpcSuccessAsync(CallInvoker callInvoker)
    {
        using AsyncUnaryCall<string> call = callInvoker.AsyncUnaryCall(
            TestMethod,
            host: null,
            new CallOptions(deadline: DateTime.UtcNow.AddSeconds(5)),
            request: "ping");

        string response = await call.ResponseAsync;
        response.Should().Be("pong");
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

    static HttpResponseMessage CreateSuccessResponse(string payload)
    {
        byte[] messageBytes = StringMarshaller.Serializer(payload);
        byte[] frame = new byte[5 + messageBytes.Length];
        uint length = (uint)messageBytes.Length;
        frame[1] = (byte)(length >> 24);
        frame[2] = (byte)(length >> 16);
        frame[3] = (byte)(length >> 8);
        frame[4] = (byte)length;
        Buffer.BlockCopy(messageBytes, 0, frame, 5, messageBytes.Length);

        HttpResponseMessage response = new(System.Net.HttpStatusCode.OK)
        {
            Version = new Version(2, 0),
            Content = new ByteArrayContent(frame),
        };

        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/grpc");
        response.TrailingHeaders.Add("grpc-status", "0");
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
