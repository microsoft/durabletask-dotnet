// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Grpc.Core;
using Microsoft.DurableTask.Client.Grpc.Internal;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DurableTask.Client.Grpc.Tests;

/// <summary>
/// Verifies that a registered <c>CallInvokerDecorator</c> is applied on every transport path the client
/// supports, and that it wraps <em>outside</em> <see cref="ChannelRecreatingCallInvoker"/> so the
/// wrapper's internal channel swaps stay transparent to the decorator.
/// </summary>
public class GrpcDurableTaskClientCallInvokerDecoratorTests
{
    static readonly MethodInfo GetCallInvokerMethod = typeof(GrpcDurableTaskClient)
        .GetMethod("GetCallInvoker", BindingFlags.Static | BindingFlags.NonPublic)!;

    [Fact]
    public async Task GetCallInvoker_ChannelPath_AppliesDecorator()
    {
        // Arrange
        GrpcChannel channel = GrpcChannel.ForAddress("http://localhost:5201");
        CallInvoker sentinel = CreateSentinel();
        GrpcDurableTaskClientOptions options = new() { Channel = channel };
        options.SetCallInvokerDecorator(_ => sentinel);

        try
        {
            // Act
            (AsyncDisposable disposable, CallInvoker callInvoker) = InvokeGetCallInvoker(options);

            // Assert
            callInvoker.Should().BeSameAs(sentinel);
            await disposable.DisposeAsync();
        }
        finally
        {
            channel.Dispose();
        }
    }

    [Fact]
    public async Task GetCallInvoker_ExternalCallInvokerPath_AppliesDecorator()
    {
        // Arrange
        CallInvoker external = CreateSentinel();
        CallInvoker sentinel = CreateSentinel();
        GrpcDurableTaskClientOptions options = new() { CallInvoker = external };
        CallInvoker? observed = null;
        options.SetCallInvokerDecorator(invoker =>
        {
            observed = invoker;
            return sentinel;
        });

        // Act
        (AsyncDisposable disposable, CallInvoker callInvoker) = InvokeGetCallInvoker(options);

        // Assert
        callInvoker.Should().BeSameAs(sentinel);
        observed.Should().BeSameAs(external);
        await disposable.DisposeAsync();
    }

    [Fact]
    public async Task GetCallInvoker_AddressPath_AppliesDecorator()
    {
        // Arrange
        CallInvoker sentinel = CreateSentinel();
        GrpcDurableTaskClientOptions options = new() { Address = "http://localhost:5202" };
        options.SetCallInvokerDecorator(_ => sentinel);

        // Act
        (AsyncDisposable disposable, CallInvoker callInvoker) = InvokeGetCallInvoker(options);

        // Assert
        callInvoker.Should().BeSameAs(sentinel);
        await disposable.DisposeAsync();
    }

    [Fact]
    public async Task GetCallInvoker_WithRecreator_AppliesDecoratorOutsideRecreatingInvoker()
    {
        // Arrange: recreation stays enabled, so the core invoker is a ChannelRecreatingCallInvoker.
        // The decorator must receive that wrapper (i.e. wrap outside it), otherwise the wrapper's
        // internal channel swaps would replace the decorated invoker and drop the interceptor.
        GrpcChannel channel = GrpcChannel.ForAddress("http://localhost:5203");
        CallInvoker sentinel = CreateSentinel();
        GrpcDurableTaskClientOptions options = new() { Channel = channel };
        options.SetChannelRecreator((existing, ct) => Task.FromResult(existing));
        CallInvoker? observed = null;
        options.SetCallInvokerDecorator(invoker =>
        {
            observed = invoker;
            return sentinel;
        });

        try
        {
            // Act
            (AsyncDisposable disposable, CallInvoker callInvoker) = InvokeGetCallInvoker(options);

            // Assert
            observed.Should().BeOfType<ChannelRecreatingCallInvoker>();
            callInvoker.Should().BeSameAs(sentinel);
            await disposable.DisposeAsync();
        }
        finally
        {
            channel.Dispose();
        }
    }

    [Fact]
    public async Task GetCallInvoker_WithoutDecorator_ReturnsUndecoratedInvoker()
    {
        // Arrange
        GrpcChannel channel = GrpcChannel.ForAddress("http://localhost:5204");
        GrpcDurableTaskClientOptions options = new() { Channel = channel };

        try
        {
            // Act
            (AsyncDisposable disposable, CallInvoker callInvoker) = InvokeGetCallInvoker(options);

            // Assert
            // Assert: with no decorator registered the invoker is exactly what core builds today.
            callInvoker.Should().BeOfType(channel.CreateCallInvoker().GetType());
            await disposable.DisposeAsync();
        }
        finally
        {
            channel.Dispose();
        }
    }

    static CallInvoker CreateSentinel() => GrpcChannel.ForAddress("http://sentinel.invalid").CreateCallInvoker();

    static (AsyncDisposable Disposable, CallInvoker CallInvoker) InvokeGetCallInvoker(
        GrpcDurableTaskClientOptions options)
    {
        object?[] args = { options, NullLogger.Instance, null };
        AsyncDisposable disposable = (AsyncDisposable)GetCallInvokerMethod.Invoke(null, args)!;
        return (disposable, (CallInvoker)args[2]!);
    }
}
