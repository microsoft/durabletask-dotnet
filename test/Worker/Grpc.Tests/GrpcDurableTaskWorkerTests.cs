// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Tests.Logging;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

public class GrpcDurableTaskWorkerTests
{
    const string Category = "Microsoft.DurableTask.Worker.Grpc";
    static readonly MethodInfo ExecuteAsyncMethod = typeof(GrpcDurableTaskWorker)
        .GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
    static readonly MethodInfo ProcessorConnectAsyncMethod = typeof(GrpcDurableTaskWorker)
        .GetNestedType("Processor", BindingFlags.NonPublic)!
        .GetMethod("ConnectAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
    static readonly MethodInfo TryRecreateChannelAsyncMethod = typeof(GrpcDurableTaskWorker)
        .GetMethod("TryRecreateChannelAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Fact]
    public async Task ExecuteAsync_ConnectFailureThreshold_RecreatesConfiguredChannel()
    {
        // Arrange
        CallbackHttpMessageHandler initialHandler = new((request, cancellationToken) =>
            Task.FromResult(CreateFailureResponse(StatusCode.Unavailable, "initial transport failure")));
        TaskCompletionSource recreatedTransportUsed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CallbackHttpMessageHandler recreatedHandler = new(async (request, cancellationToken) =>
        {
            recreatedTransportUsed.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return CreateFailureResponse(StatusCode.Cancelled, "recreated transport cancelled");
        });

        GrpcChannel currentChannel = CreateChannel("http://initial.worker.test", initialHandler);
        GrpcChannel recreatedChannel = CreateChannel("http://recreated.worker.test", recreatedHandler);
        GrpcDurableTaskWorkerOptions grpcOptions = new()
        {
            Channel = currentChannel,
        };
        grpcOptions.Internal.ChannelRecreateFailureThreshold = 2;
        grpcOptions.Internal.ReconnectBackoffBase = TimeSpan.Zero;
        grpcOptions.Internal.ReconnectBackoffCap = TimeSpan.Zero;

        DurableTaskWorkerOptions workerOptions = new()
        {
            Logging = { UseLegacyCategories = false },
        };
        TestLogProvider logProvider = new(new NullOutput());
        using CancellationTokenSource stoppingToken = new();
        int recreatorCalls = 0;
        grpcOptions.SetChannelRecreator((channel, ct) =>
        {
            recreatorCalls++;
            return Task.FromResult(recreatedChannel);
        });

        GrpcDurableTaskWorker worker = CreateWorker(grpcOptions, workerOptions, new SimpleLoggerFactory(logProvider));

        try
        {
            // Act
            Task executeTask = InvokeExecuteAsync(worker, stoppingToken.Token);
            await recreatedTransportUsed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            stoppingToken.Cancel();
            await executeTask;

            // Assert
            recreatorCalls.Should().Be(1);
            initialHandler.CallCount.Should().Be(2);
            recreatedHandler.CallCount.Should().Be(1);
            logProvider.TryGetLogs(Category, out IReadOnlyCollection<LogEntry>? logs).Should().BeTrue();
            logs!.Should().Contain(log =>
                log.Message.Contains("gRPC channel to backend has been recreated")
                && log.Message.Contains(recreatedChannel.Target));
        }
        finally
        {
            await DisposeChannelAsync(currentChannel);
            await DisposeChannelAsync(recreatedChannel);
        }
    }

    [Fact]
    public async Task TryRecreateChannelAsync_ConfiguredRecreatorReturningSameChannel_DoesNotRecreate()
    {
        // Arrange
        GrpcChannel currentChannel = GrpcChannel.ForAddress("http://localhost:5003");
        GrpcDurableTaskWorkerOptions grpcOptions = new()
        {
            Channel = currentChannel,
        };

        GrpcChannel? observedChannel = null;
        grpcOptions.SetChannelRecreator((channel, ct) =>
        {
            observedChannel = channel;
            return Task.FromResult(channel);
        });

        GrpcDurableTaskWorker worker = CreateWorker(grpcOptions);

        try
        {
            // Act
            object result = await InvokeTryRecreateChannelAsync(worker, currentChannel);

            // Assert
            observedChannel.Should().BeSameAs(currentChannel);
            GetResultProperty<bool>(result, "Recreated").Should().BeFalse();
            GetResultProperty<GrpcChannel?>(result, "NewChannel").Should().BeNull();
            GetResultProperty<string?>(result, "NewAddress").Should().BeNull();
        }
        finally
        {
            await DisposeChannelAsync(currentChannel);
        }
    }

    [Fact]
    public async Task TryRecreateChannelAsync_ConfiguredRecreatorReturningDifferentChannel_DoesNotCarryForwardOldDisposable()
    {
        // Arrange
        GrpcChannel currentChannel = GrpcChannel.ForAddress("http://localhost:5004");
        GrpcChannel recreatedChannel = GrpcChannel.ForAddress("http://localhost:5005");
        GrpcDurableTaskWorkerOptions grpcOptions = new()
        {
            Channel = currentChannel,
        };
        grpcOptions.SetChannelRecreator((channel, ct) => Task.FromResult(recreatedChannel));

        GrpcDurableTaskWorker worker = CreateWorker(grpcOptions);
        int disposeCalls = 0;
        AsyncDisposable currentWorkerOwnedDisposable = new(() =>
        {
            Interlocked.Increment(ref disposeCalls);
            return ValueTask.CompletedTask;
        });

        try
        {
            // Act
            object result = await InvokeTryRecreateChannelAsync(worker, currentWorkerOwnedDisposable, currentChannel);
            AsyncDisposable newDisposable = GetResultProperty<AsyncDisposable>(result, "NewWorkerOwnedDisposable");

            // Simulate the outer worker handoff.
            await currentWorkerOwnedDisposable.DisposeAsync();
            await newDisposable.DisposeAsync();

            // Assert
            GetResultProperty<bool>(result, "Recreated").Should().BeTrue();
            GetResultProperty<GrpcChannel?>(result, "NewChannel").Should().BeSameAs(recreatedChannel);
            Volatile.Read(ref disposeCalls).Should().Be(1);
        }
        finally
        {
            await DisposeChannelAsync(currentChannel);
            await DisposeChannelAsync(recreatedChannel);
        }
    }

    [Fact]
    public async Task ConnectAsync_VeryLargeHelloDeadline_UsesUtcMaxValueDeadline()
    {
        // Arrange
        GrpcDurableTaskWorkerOptions grpcOptions = new();
        grpcOptions.SetHelloDeadline(TimeSpan.MaxValue);
        GrpcDurableTaskWorker worker = CreateWorker(grpcOptions);
        RecordingCallInvoker callInvoker = new();
        P.TaskHubSidecarService.TaskHubSidecarServiceClient client = new(callInvoker);
        object processor = CreateProcessor(worker, client);

        // Act
        using AsyncServerStreamingCall<P.WorkItem> stream = await InvokeProcessorConnectAsync(processor);

        // Assert
        callInvoker.HelloDeadline.Should().HaveValue();
        DateTime deadline = callInvoker.HelloDeadline!.Value;
        deadline.Kind.Should().Be(DateTimeKind.Utc);
        deadline.Should().Be(DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));
    }

    static GrpcDurableTaskWorker CreateWorker(GrpcDurableTaskWorkerOptions grpcOptions)
    {
        return CreateWorker(grpcOptions, new DurableTaskWorkerOptions(), NullLoggerFactory.Instance);
    }

    static GrpcDurableTaskWorker CreateWorker(
        GrpcDurableTaskWorkerOptions grpcOptions,
        DurableTaskWorkerOptions workerOptions,
        ILoggerFactory loggerFactory)
    {
        Mock<IDurableTaskFactory> factoryMock = new(MockBehavior.Strict);

        return new GrpcDurableTaskWorker(
            name: "Test",
            factory: factoryMock.Object,
            grpcOptions: new OptionsMonitorStub<GrpcDurableTaskWorkerOptions>(grpcOptions),
            workerOptions: new OptionsMonitorStub<DurableTaskWorkerOptions>(workerOptions),
            services: Mock.Of<IServiceProvider>(),
            loggerFactory: loggerFactory,
            orchestrationFilter: null,
            exceptionPropertiesProvider: null);
    }

    static Task InvokeExecuteAsync(GrpcDurableTaskWorker worker, CancellationToken cancellationToken)
    {
        return (Task)ExecuteAsyncMethod.Invoke(worker, new object?[] { cancellationToken })!;
    }

    static object CreateProcessor(GrpcDurableTaskWorker worker, P.TaskHubSidecarService.TaskHubSidecarServiceClient client)
    {
        System.Type processorType = typeof(GrpcDurableTaskWorker).GetNestedType("Processor", BindingFlags.NonPublic)!;
        return Activator.CreateInstance(
            processorType,
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            args: new object?[] { worker, client, null, null },
            culture: null)!;
    }

    static async Task<AsyncServerStreamingCall<P.WorkItem>> InvokeProcessorConnectAsync(object processor)
    {
        Task task = (Task)ProcessorConnectAsyncMethod.Invoke(processor, new object?[] { CancellationToken.None })!;
        await task;
        return (AsyncServerStreamingCall<P.WorkItem>)task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    static async Task<object> InvokeTryRecreateChannelAsync(GrpcDurableTaskWorker worker, GrpcChannel currentChannel)
    {
        return await InvokeTryRecreateChannelAsync(worker, default, currentChannel);
    }

    static async Task<object> InvokeTryRecreateChannelAsync(
        GrpcDurableTaskWorker worker,
        AsyncDisposable currentWorkerOwnedDisposable,
        GrpcChannel currentChannel)
    {
        object?[] args = { CancellationToken.None, currentWorkerOwnedDisposable, currentChannel };
        Task task = (Task)TryRecreateChannelAsyncMethod.Invoke(worker, args)!;
        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    static T GetResultProperty<T>(object result, string propertyName)
    {
        return (T)result.GetType().GetProperty(propertyName)!.GetValue(result)!;
    }

    static async ValueTask DisposeChannelAsync(GrpcChannel channel)
    {
        await channel.ShutdownAsync();
        channel.Dispose();
    }

    static GrpcChannel CreateChannel(string address, HttpMessageHandler handler)
    {
        return GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            HttpHandler = handler,
        });
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

    sealed class RecordingCallInvoker : CallInvoker
    {
        public DateTime? HelloDeadline { get; private set; }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            throw new NotSupportedException();
        }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            if (method.FullName == "/TaskHubSidecarService/Hello")
            {
                this.HelloDeadline = options.Deadline;
                TResponse response = (TResponse)(object)new Empty();
                return new AsyncUnaryCall<TResponse>(
                    Task.FromResult(response),
                    Task.FromResult(new Metadata()),
                    () => new Status(StatusCode.OK, string.Empty),
                    () => new Metadata(),
                    () => { });
            }

            throw new NotSupportedException($"Unexpected unary method {method.FullName}.");
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            if (method.FullName == "/TaskHubSidecarService/GetWorkItems")
            {
                return new AsyncServerStreamingCall<TResponse>(
                    new EmptyAsyncStreamReader<TResponse>(),
                    Task.FromResult(new Metadata()),
                    () => new Status(StatusCode.OK, string.Empty),
                    () => new Metadata(),
                    () => { });
            }

            throw new NotSupportedException($"Unexpected server-streaming method {method.FullName}.");
        }

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options)
        {
            throw new NotSupportedException();
        }

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options)
        {
            throw new NotSupportedException();
        }
    }

    sealed class EmptyAsyncStreamReader<T> : IAsyncStreamReader<T>
    {
        public T Current => default!;

        public Task<bool> MoveNext(CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
