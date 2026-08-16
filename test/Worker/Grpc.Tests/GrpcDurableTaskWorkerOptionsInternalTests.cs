// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.DurableTask.Worker.Grpc.Internal;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

public class GrpcDurableTaskWorkerOptionsInternalTests
{
    [Fact]
    public void InternalOptions_HasSafeDefaults()
    {
        // Arrange
        GrpcDurableTaskWorkerOptions options = new();

        // Act
        GrpcDurableTaskWorkerOptions.InternalOptions internalOptions = options.Internal;

        // Assert
        internalOptions.HelloDeadline.Should().Be(TimeSpan.FromSeconds(30));
        internalOptions.ChannelRecreateFailureThreshold.Should().Be(5);
        internalOptions.ReconnectBackoffBase.Should().Be(TimeSpan.FromSeconds(1));
        internalOptions.ReconnectBackoffCap.Should().Be(TimeSpan.FromSeconds(30));
        internalOptions.TransientRetryBackoffBase.Should().Be(TimeSpan.FromMilliseconds(200));
        internalOptions.TransientRetryBackoffCap.Should().Be(TimeSpan.FromSeconds(15));
        internalOptions.TransientRetryMaxAttempts.Should().Be(10);
        internalOptions.SilentDisconnectTimeout.Should().Be(TimeSpan.FromSeconds(120));
        internalOptions.ChannelRecreator.Should().BeNull();
        internalOptions.CallInvokerDecorator.Should().BeNull();
    }

    [Fact]
    public void SetCallInvokerDecorator_NullCallback_Throws()
    {
        // Arrange
        GrpcDurableTaskWorkerOptions options = new();

        // Act
        Action act = () => options.SetCallInvokerDecorator(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ApplyCallInvokerDecorator_NoDecorator_ReturnsOriginalInvoker()
    {
        // Arrange
        GrpcDurableTaskWorkerOptions options = new();
        CallInvoker invoker = GrpcChannel.ForAddress("http://localhost:9101").CreateCallInvoker();

        // Act
        CallInvoker result = options.ApplyCallInvokerDecorator(invoker);

        // Assert
        result.Should().BeSameAs(invoker);
    }

    [Fact]
    public void ApplyCallInvokerDecorator_WithDecorator_ReturnsDecoratedInvoker()
    {
        // Arrange
        GrpcDurableTaskWorkerOptions options = new();
        CallInvoker invoker = GrpcChannel.ForAddress("http://localhost:9102").CreateCallInvoker();
        CallInvoker decorated = GrpcChannel.ForAddress("http://localhost:9103").CreateCallInvoker();
        CallInvoker? observed = null;
        options.SetCallInvokerDecorator(inner =>
        {
            observed = inner;
            return decorated;
        });

        // Act
        CallInvoker result = options.ApplyCallInvokerDecorator(invoker);

        // Assert
        result.Should().BeSameAs(decorated);
        observed.Should().BeSameAs(invoker);
    }

    [Fact]
    public void SetChannelRecreator_NullCallback_Throws()
    {
        // Arrange
        GrpcDurableTaskWorkerOptions options = new();

        // Act
        Action act = () => options.SetChannelRecreator(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SetChannelRecreator_StoresCallbackOnInternalOptions()
    {
        // Arrange
        GrpcDurableTaskWorkerOptions options = new();
        bool invoked = false;
        Func<GrpcChannel, CancellationToken, Task<GrpcChannel>> recreator = (channel, ct) =>
        {
            invoked = true;
            return Task.FromResult(channel);
        };

        // Act
        options.SetChannelRecreator(recreator);

        // Assert
        options.Internal.ChannelRecreator.Should().BeSameAs(recreator);

        // Sanity-check that invoking the stored delegate calls the original.
        options.Internal.ChannelRecreator!.Invoke(null!, CancellationToken.None);
        invoked.Should().BeTrue();
    }
}
