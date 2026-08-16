// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Grpc.Core;
using Microsoft.DurableTask.Worker.Grpc.Internal;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

/// <summary>
/// Verifies that a registered <c>CallInvokerDecorator</c> is applied everywhere the worker produces a
/// <see cref="CallInvoker"/> — including invokers rebuilt after a channel recreate — so extensions can
/// install interceptors without clearing <c>Channel</c> and disabling channel recreation.
/// </summary>
public class GrpcDurableTaskWorkerCallInvokerDecoratorTests
{
    static readonly MethodInfo GetCallInvokerMethod = typeof(GrpcDurableTaskWorker)
        .GetMethod("GetCallInvoker", BindingFlags.Instance | BindingFlags.NonPublic)!;
    static readonly MethodInfo TryRecreateChannelAsyncMethod = typeof(GrpcDurableTaskWorker)
        .GetMethod("TryRecreateChannelAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Fact]
    public void GetCallInvoker_WithDecorator_ReturnsDecoratedInvoker()
    {
        // Arrange
        GrpcChannel channel = GrpcChannel.ForAddress("http://localhost:5101");
        CallInvoker sentinel = CreateSentinel();
        GrpcDurableTaskWorkerOptions grpcOptions = new() { Channel = channel };
        CallInvoker? observed = null;
        grpcOptions.SetCallInvokerDecorator(invoker =>
        {
            observed = invoker;
            return sentinel;
        });

        GrpcDurableTaskWorker worker = CreateWorker(grpcOptions);

        try
        {
            // Act
            InvokeGetCallInvoker(worker, out CallInvoker callInvoker, out string address);

            // Assert
            callInvoker.Should().BeSameAs(sentinel);
            observed.Should().NotBeNull().And.NotBeSameAs(sentinel);
            address.Should().Be(channel.Target);
        }
        finally
        {
            DisposeChannel(channel);
        }
    }

    [Fact]
    public void GetCallInvoker_WithoutDecorator_ReturnsUndecoratedInvoker()
    {
        // Arrange
        GrpcChannel channel = GrpcChannel.ForAddress("http://localhost:5102");
        GrpcDurableTaskWorkerOptions grpcOptions = new() { Channel = channel };
        GrpcDurableTaskWorker worker = CreateWorker(grpcOptions);

        try
        {
            // Act
            InvokeGetCallInvoker(worker, out CallInvoker callInvoker, out string address);

            // Assert
            // Assert: with no decorator registered the invoker is exactly what the channel produces.
            callInvoker.Should().BeOfType(channel.CreateCallInvoker().GetType());
            address.Should().Be(channel.Target);
        }
        finally
        {
            DisposeChannel(channel);
        }
    }

    [Fact]
    public async Task TryRecreateChannelAsync_ChannelWithRecreatorAndDecorator_RecreatesAndDecorates()
    {
        // Arrange: this is the shape the AzureBlobPayloads extension used to break — a DTS-configured
        // Channel plus recreator. Path 1 requires Channel to still be set, and the invoker the worker
        // builds from the replacement channel must still carry the decorator.
        GrpcChannel currentChannel = GrpcChannel.ForAddress("http://localhost:5103");
        GrpcChannel recreatedChannel = GrpcChannel.ForAddress("http://localhost:5104");
        CallInvoker sentinel = CreateSentinel();
        GrpcDurableTaskWorkerOptions grpcOptions = new() { Channel = currentChannel };
        grpcOptions.SetChannelRecreator((channel, ct) => Task.FromResult(recreatedChannel));
        grpcOptions.SetCallInvokerDecorator(_ => sentinel);

        GrpcDurableTaskWorker worker = CreateWorker(grpcOptions);

        try
        {
            // Act
            object result = await InvokeTryRecreateChannelAsync(worker, currentChannel);

            // Assert
            GetResultProperty<bool>(result, "Recreated").Should().BeTrue();
            GetResultProperty<GrpcChannel?>(result, "NewChannel").Should().BeSameAs(recreatedChannel);
            GetResultProperty<CallInvoker?>(result, "NewCallInvoker").Should().BeSameAs(sentinel);
        }
        finally
        {
            DisposeChannel(currentChannel);
            DisposeChannel(recreatedChannel);
        }
    }

    [Fact]
    public async Task TryRecreateChannelAsync_WorkerOwnedChannelWithDecorator_DecoratesRebuiltInvoker()
    {
        // Arrange: Address-only configuration takes the worker-owned rebuild path.
        CallInvoker sentinel = CreateSentinel();
        GrpcDurableTaskWorkerOptions grpcOptions = new() { Address = "http://localhost:5105" };
        grpcOptions.SetCallInvokerDecorator(_ => sentinel);

        GrpcDurableTaskWorker worker = CreateWorker(grpcOptions);
        GrpcChannel currentChannel = GrpcChannel.ForAddress(grpcOptions.Address);

        try
        {
            // Act
            object result = await InvokeTryRecreateChannelAsync(worker, currentChannel);

            // Assert
            GetResultProperty<bool>(result, "Recreated").Should().BeTrue();
            GetResultProperty<CallInvoker?>(result, "NewCallInvoker").Should().BeSameAs(sentinel);

            AsyncDisposable newDisposable = GetResultProperty<AsyncDisposable>(result, "NewWorkerOwnedDisposable");
            await newDisposable.DisposeAsync();
        }
        finally
        {
            DisposeChannel(currentChannel);
        }
    }

    static CallInvoker CreateSentinel() => GrpcChannel.ForAddress("http://sentinel.invalid").CreateCallInvoker();

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

    static void DisposeChannel(GrpcChannel channel) => channel.Dispose();

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
}
